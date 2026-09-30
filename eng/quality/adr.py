"""Validate the repository's restricted MADR profile and derive both decision indexes.

The parser deliberately supports flat metadata and inline Markdown links only. It
never evaluates YAML tags, follows remote links, or executes confirmation commands.
"""

import argparse
import datetime
import json
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from urllib.parse import unquote, urlsplit

KEYS = (
    "id", "status", "date", "decision-makers", "consulted", "informed", "scope",
    "supersedes", "superseded-by", "amends", "amended-by", "madr-version", "doka-profile-version",
)
LIST_KEYS = {"decision-makers", "consulted", "informed", "supersedes", "superseded-by", "amends", "amended-by"}
RELATIONSHIPS = {"supersedes": "superseded-by", "superseded-by": "supersedes",
                 "amends": "amended-by", "amended-by": "amends"}
TRANSITIONS = {"proposed": {"accepted", "rejected"}, "accepted": {"implemented", "deprecated", "superseded"},
               "implemented": {"deprecated", "superseded"}, "rejected": set(), "deprecated": set(), "superseded": set()}
BEFORE_OPTIONS = [
    (2, "Context and Problem Statement"), (2, "Decision Drivers"), (2, "Considered Options"),
    (2, "Decision Outcome"), (3, "Consequences"), (3, "Confirmation"), (2, "Pros and Cons of the Options"),
]
AFTER_OPTIONS = [(2, "More Information"), (3, "Re-evaluation Triggers"), (3, "Decision History"),
                 (3, "Implementation References"), (3, "Sources")]
ID = re.compile(r"D-[0-9]{3}\Z")
FILENAME = re.compile(r"(D-[0-9]{3})-[a-z0-9]+(?:[-.][a-z0-9]+)*\.md\Z")
LINK = re.compile(r"\[([^\[\]\n]+)\]\(([^\s()]+)\)")
REPOSITORY_ONLY = "- No external sources; repository evidence only."


@dataclass(frozen=True)
class Decision:
    """One parsed record whose metadata remains the authoritative index input."""

    path: Path
    metadata: dict
    title: str


def is_date(value):
    """Require canonical real calendar dates rather than shape-only timestamps."""
    try:
        return bool(re.fullmatch(r"[0-9]{4}-[0-9]{2}-[0-9]{2}", value)) and bool(datetime.date.fromisoformat(value))
    except (ValueError, TypeError):
        return False


def scalar(value):
    """Parse one explicit quoted string or a conservative plain metadata token."""
    if value.startswith('"'):
        result = json.loads(value)
        if not isinstance(result, str) or not result.strip() or any(ord(char) < 32 or ord(char) >= 127 for char in result):
            raise ValueError("expected a nonempty scalar string")

        return result

    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9 ._-]*", value):
        raise ValueError("expected a plain or double-quoted scalar")

    return value


def metadata_value(key, value):
    """Parse unique inline list elements without introducing a general YAML parser."""
    if key not in LIST_KEYS:
        return scalar(value)

    if not value.startswith("[") or not value.endswith("]"):
        raise ValueError("expected an inline list")

    inner = value[1:-1].strip()
    if not inner:
        return []

    # WHY: Quoted elements may contain commas; a quote-aware scan avoids silently
    # interpreting one participant as two while still rejecting nested syntax.
    token = r'"(?:[^"\\]|\\.)*"|[A-Za-z0-9][A-Za-z0-9 ._-]*'
    if not re.fullmatch(rf'\s*(?:{token})(?:\s*,\s*(?:{token}))*\s*', inner):
        raise ValueError("malformed inline list")

    elements = re.findall(token, inner)
    result = [scalar(element.strip()) for element in elements]
    if len(result) != len(set(result)):
        raise ValueError("duplicate list element")

    return result


def read_ascii(path, errors):
    """Read canonical source bytes and accumulate actionable file diagnostics."""
    try:
        content = path.read_bytes()
        text = content.decode("ascii")
    except (OSError, UnicodeError) as error:
        errors.append(f"{path}:1: cannot read ASCII source: {error}")
        return None

    if b"\r" in content or not content.endswith(b"\n"):
        errors.append(f"{path}:1: require LF line endings and a final newline")

    if any((byte < 32 and byte not in (9, 10, 13)) or byte == 127 for byte in content):
        errors.append(f"{path}:1: control characters are forbidden")

    return text


def headings(lines, start, report):
    """Find actual ATX headings, excluding fenced examples from document structure."""
    found = []
    fence = None
    for index in range(start, len(lines)):
        line = lines[index]
        marker = re.match(r"^(`{3,}|~{3,})", line)
        if marker:
            token = marker.group(1)
            if fence is None:
                fence = token
            elif token[0] == fence[0] and len(token) >= len(fence):
                fence = None

            continue

        if fence is not None:
            continue

        match = re.match(r"^(#{1,6}) (.+)$", line)
        if match:
            if index and lines[index - 1].strip():
                report(index + 1, "heading requires a preceding blank line")

            found.append((len(match.group(1)), match.group(2), index))

    if fence is not None:
        report(len(lines), "unclosed Markdown fence")

    return found


def check_history(text, metadata, report):
    """Replay the chronological decision state chain without inferring approval."""
    entries = [line for line in text.splitlines() if line.strip()]
    state = None
    previous_date = ""
    for index, line in enumerate(entries):
        match = re.fullmatch(r"- ([0-9-]+): (.+)", line)
        if not match or not is_date(match.group(1)):
            report("history requires a real dated bullet")
            continue

        date, description = match.groups()
        if date < previous_date:
            report("history dates must be nondecreasing")

        previous_date = date
        if index == 0:
            if date != metadata.get("date") or description != "Decision recorded with status proposed.":
                report("history must start on metadata date with Decision recorded with status proposed.")

            state = "proposed"
            continue

        transition = re.fullmatch(r"Status changed from ([a-z]+) to ([a-z]+)\.", description)
        if transition:
            old, new = transition.groups()
            if old != state or new not in TRANSITIONS.get(old, set()):
                report(f"illegal history transition {old} -> {new}")

            state = new
        elif "Status changed" in description or "Decision recorded with status" in description:
            report("status history must use the exact transition syntax")

    if state != metadata.get("status"):
        report("history final state does not match metadata status")


def check_links(lines, source_line, implementation, path, root, report):
    """Constrain source provenance and require repository-contained local evidence."""
    for index, line in enumerate(lines):
        if re.search(r"\]\[|^\s*\[[^\]]+\]:", line):
            report(index + 1, "reference links are unsupported; use explicit inline links")

        if index < source_line and re.search(r"(?:[A-Za-z][A-Za-z0-9+.-]*://|www\.)", line):
            report(index + 1, "external URLs belong only under Sources")

        for match in LINK.finditer(line):
            destination = match.group(2)
            # WHY: URL parsing permits encoded NULs, but filesystem resolution
            # raises ValueError instead of returning a missing-path diagnostic.
            if "\x00" in unquote(destination):
                report(index + 1, "link destinations must not contain null bytes")
                continue

            try:
                parsed = urlsplit(destination)
            except ValueError:
                report(index + 1, "malformed link destination")
                continue

            if parsed.scheme or parsed.netloc:
                if parsed.scheme != "https" or not parsed.netloc or parsed.username or parsed.password:
                    report(index + 1, "external links require HTTPS without credentials")

                continue

            if destination.startswith("/") or "\\" in destination or parsed.query:
                report(index + 1, "local links require a relative repository path without a query")
                continue

            try:
                target = (path.parent / unquote(parsed.path)).resolve()
            except (OSError, RuntimeError):
                report(index + 1, f"local link cannot resolve: {destination}")
                continue

            if not target.is_relative_to(root.resolve()) or not target.exists():
                report(index + 1, f"local link does not resolve inside repository: {destination}")

    if not LINK.search(implementation):
        report(source_line, "Implementation References requires at least one concrete local link")
    elif not any(not re.match(r"[A-Za-z][A-Za-z0-9+.-]*:|//|#", match.group(2))
                 for match in LINK.finditer(implementation)):
        report(source_line, "Implementation References must include repository evidence")


def parse_decision(path, root, errors):
    """Validate one record, preserving other records' diagnostics after malformed input."""
    def report(line, message):
        errors.append(f"{path}:{line}: {message}")

    try:
        contained = path.resolve().is_relative_to(root)
    except (OSError, RuntimeError):
        contained = False

    if not contained:
        report(1, "decision source must resolve inside repository")
        return None

    text = read_ascii(path, errors)
    if text is None:
        return None

    lines = text.splitlines()
    filename = FILENAME.fullmatch(path.name)
    if filename is None or path.parent != root / "docs" / "decisions":
        report(1, "invalid decision filename or nested decision location")

    if not lines or lines[0] != "---" or "---" not in lines[1:]:
        report(1, "missing flat front matter delimiters")
        return None

    end = lines.index("---", 1)
    metadata = {}
    observed_keys = []
    for index, line in enumerate(lines[1:end], 2):
        match = re.fullmatch(r"([a-z-]+): (.+)", line)
        if not match:
            report(index, "metadata requires one flat key: value per line")
            continue

        key, value = match.groups()
        observed_keys.append(key)
        if key not in KEYS or key in metadata:
            report(index, f"unknown or duplicate metadata key: {key}")
            continue

        try:
            metadata[key] = metadata_value(key, value)
            values = metadata[key] if isinstance(metadata[key], list) else [metadata[key]]
            if any(re.search(r"[A-Za-z][A-Za-z0-9+.-]*://|www\.", item) for item in values):
                report(index, "external URLs belong only under Sources, not metadata")
        except (ValueError, json.JSONDecodeError) as error:
            report(index, f"invalid {key}: {error}")

    if tuple(observed_keys) != KEYS:
        report(2, "metadata keys must appear exactly once in profile order")

    if not ID.fullmatch(metadata.get("id", "")) or (filename and filename.group(1) != metadata.get("id")):
        report(2, "filename and metadata identity must match D-NNN")

    if metadata.get("status") not in TRANSITIONS:
        report(3, "unsupported decision status")

    if not is_date(metadata.get("date")):
        report(4, "invalid recording date")

    if not metadata.get("decision-makers") or not metadata.get("scope"):
        report(5, "decision-makers and scope must not be empty")

    for key, version in (("madr-version", "4.0.0"), ("doka-profile-version", "1.0")):
        if metadata.get(key) != version:
            report(2, f"{key} must be {version}")

    for key in RELATIONSHIPS:
        for target in metadata.get(key, []):
            if not ID.fullmatch(target):
                report(2, f"{key} must contain D-NNN identifiers")

    found = headings(lines, end + 1, report)
    title = ""
    if found:
        match = re.fullmatch(r"(D-[0-9]{3}) -- (.+)", found[0][1])
        if found[0][0] != 1 or not match or match.group(1) != metadata.get("id"):
            report(found[0][2] + 1, "H1 must match # D-NNN -- Short decision title")
        else:
            title = match.group(2)
            if any(character in title for character in "|[]`<>"):
                report(found[0][2] + 1, "title contains unsupported index markup")
    else:
        report(end + 1, "missing decision title and sections")

    sections = {}
    section_lines = {}
    for offset, (_, name, index) in enumerate(found):
        stop = found[offset + 1][2] if offset + 1 < len(found) else len(lines)
        sections[name] = "\n".join(lines[index + 1:stop]).strip()
        section_lines[name] = index + 1

    options = [line[2:] for line in sections.get("Considered Options", "").splitlines() if line.startswith("- ")]
    if len(options) < 2 or len(options) != len(set(options)) or any(not option.strip() for option in options):
        report(section_lines.get("Considered Options", 1), "require at least two distinct considered options")

    expected = BEFORE_OPTIONS + [(3, option) for option in options] + AFTER_OPTIONS
    if [(level, name) for level, name, _ in found[1:]] != expected:
        report(end + 1, "section hierarchy, order, uniqueness, or exact option headings do not match profile")

    for _, name in expected:
        if name != "Pros and Cons of the Options" and not sections.get(name):
            report(section_lines.get(name, 1), f"empty or missing section: {name}")

    outcome = " ".join(sections.get("Decision Outcome", "").split())
    chosen = re.search(r'^Chosen option: "([^"]+)", because\s+\S', outcome)
    if not chosen or chosen.group(1) not in options or outcome.count("Chosen option:") != 1:
        report(section_lines.get("Decision Outcome", 1), "outcome must choose one exact considered option with a reason")

    for name in ["Consequences"] + options:
        for direction in ("Good", "Bad"):
            if not re.search(rf"^- {direction}, because \S.+", sections.get(name, ""), re.MULTILINE):
                report(section_lines.get(name, 1), f"{name} requires a {direction}, because trade-off")

    confirmation = sections.get("Confirmation", "")
    if not re.search(r"`[^`\n]+`|\[[^\]]+\]\([^\s]+\)", confirmation) or not re.search(
            r"\b(expect|expected|must|reject|rejects|pass|passes)\b", confirmation, re.IGNORECASE):
        report(section_lines.get("Confirmation", 1), "Confirmation requires concrete evidence and an expected result")

    for index, line in enumerate(lines[end + 1:], end + 2):
        if re.match(r"^\s*(?:[-*] )?(?:\*\*)?(?:status|date|scope)(?:\*\*)?\s*:", line, re.IGNORECASE) or re.search(
                r"original record metadata", line, re.IGNORECASE):
            report(index, "body must not duplicate current metadata")

    check_history(sections.get("Decision History", ""), metadata,
                  lambda message: report(section_lines.get("Decision History", 1), message))
    source_line = section_lines.get("Sources", len(lines) + 1)
    sources = sections.get("Sources", "")
    if sources != REPOSITORY_ONLY:
        for source in sources.splitlines():
            if not source.strip():
                continue

            match = re.fullmatch(r"- \[[^\]]+\]\((https://[^\s()]+)\) \(primary source; retrieved ([0-9-]+)\)", source)
            if not match or not is_date(match.group(2)):
                report(source_line, "Sources requires dated HTTPS primary entries or the exclusive repository-only marker")

    check_links(lines[end + 1:], source_line - end - 1, sections.get("Implementation References", ""), path, root,
                lambda line, message: report(line + end + 1, message))

    return Decision(path, metadata, title)


def check_relationships(decisions, errors):
    """Check reciprocal edges and status invariants after loading the whole corpus."""
    by_id = {decision.metadata.get("id"): decision for decision in decisions}
    expected = [f"D-{number:03}" for number in range(1, len(decisions) + 1)]
    if sorted(decision.metadata.get("id", "") for decision in decisions) != expected:
        errors.append("docs/decisions:1: identifiers must be unique and contiguous from D-001")

    for decision in decisions:
        metadata = decision.metadata
        superseded = metadata.get("status") == "superseded"
        if superseded != bool(metadata.get("superseded-by")):
            errors.append(f"{decision.path}:1: superseded status and superseded-by must agree")

        for relationship, reciprocal in RELATIONSHIPS.items():
            for target in metadata.get(relationship, []):
                other = by_id.get(target)
                if target == metadata.get("id") or other is None:
                    errors.append(f"{decision.path}:1: {relationship} target is missing or self-referential: {target}")
                elif metadata.get("id") not in other.metadata.get(reciprocal, []):
                    errors.append(f"{decision.path}:1: {relationship} {target} lacks reciprocal {reciprocal}")


def render_indexes(decisions, root):
    """Render stable human and machine navigation from the same ordered records."""
    records = sorted(decisions, key=lambda decision: decision.metadata["id"])
    index = {"schemaVersion": 1, "madrVersion": "4.0.0", "dokaProfileVersion": "1.0", "decisions": []}
    lines = ["# Architecture decisions", "", "<!-- Generated by eng/validate-adrs.sh --write-index. Do not edit. -->", "",
             "These records use [MADR 4.0.0 with the Doka profile](MADR-PROFILE.md). Start from the",
             "[template](adr-template.md). ADR front matter is authoritative; this page and",
             "[decision-index.json](decision-index.json) are deterministic navigation views.", "",
             "A proposed record is open for review. Recording an existing implementation does not",
             "establish historical approval, consultation, or release qualification. Recording dates",
             "identify when the record was created; decision history carries later status changes.", "",
             "Validate with `eng/validate-adrs.sh`; regenerate with `eng/validate-adrs.sh --write-index`.", "",
             "| ID | Status | Recording date | Title | Relationships |", "| --- | --- | --- | --- | --- |"]
    edges = []
    for decision in records:
        metadata = decision.metadata
        record = {"id": metadata["id"], "title": decision.title, "status": metadata["status"], "date": metadata["date"],
                  "path": decision.path.relative_to(root).as_posix()}
        relations = []
        for key, json_key in (("supersedes", "supersedes"), ("superseded-by", "supersededBy"),
                              ("amends", "amends"), ("amended-by", "amendedBy")):
            record[json_key] = sorted(metadata[key])
            relations.extend(f"{key} {target}" for target in sorted(metadata[key]))
            if key in ("supersedes", "amends"):
                edges.extend((metadata["id"], key, target) for target in metadata[key])

        index["decisions"].append(record)
        lines.append(f"| [{metadata['id']}]({decision.path.name}) | {metadata['status']} | {metadata['date']} | "
                     f"{decision.title} | {'; '.join(relations) if relations else 'None'} |")

    lines.extend(["", "## Relationships", ""])
    if edges:
        lines.extend(["```mermaid", "flowchart LR"])
        for source, verb, target in sorted(edges):
            lines.append(f'    {source.replace("-", "_")}["{source}"] -->|{verb}| {target.replace("-", "_")}["{target}"]')

        lines.append("```")
    else:
        lines.append("No amendment or supersession relationships have been recorded.")

    return {"README.md": "\n".join(lines) + "\n", "decision-index.json": json.dumps(index, indent=2) + "\n"}


def validate_repository(root, write_index=False):
    """Return all corpus errors; only explicit write mode may update derived indexes."""
    root = Path(root).resolve()
    directory = root / "docs" / "decisions"
    errors = []
    for name in ("MADR-PROFILE.md", "adr-template.md"):
        read_ascii(directory / name, errors)

    paths = sorted(directory.rglob("D-*.md"))
    if not paths:
        errors.append(f"{directory}:1: decision corpus is missing or empty")

    decisions = [decision for path in paths if (decision := parse_decision(path, root, errors)) is not None]
    check_relationships(decisions, errors)
    if errors:
        return errors

    # WHY: Never regenerate after a malformed corpus; that would replace the last
    # reviewable index with a partial account that hides the invalid record.
    for name, expected in render_indexes(decisions, root).items():
        path = directory / name
        if path.is_symlink():
            errors.append(f"{path}:1: generated indexes must not be symbolic links")
            continue

        try:
            if write_index:
                path.write_bytes(expected.encode("ascii"))
            elif not path.exists() or path.read_bytes() != expected.encode("ascii"):
                errors.append(f"{path}:1: generated index is missing or stale; run eng/validate-adrs.sh --write-index")
        except OSError as error:
            errors.append(f"{path}:1: cannot access generated index: {error}")

    return errors


def main(arguments=None):
    """Expose read-only validation by default and a deliberate index-write command."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2], help="repository root")
    parser.add_argument("--write-index", action="store_true", help="regenerate indexes after successful corpus validation")
    options = parser.parse_args(arguments)
    errors = validate_repository(options.root, options.write_index)
    if errors:
        print("\n".join(errors), file=sys.stderr)
        return 1

    action = "Validated and regenerated" if options.write_index else "Validated"
    print(f"{action} MADR 4.0.0 / Doka profile 1.0 decisions and indexes.")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())

"""Shared identity and file-integrity contracts for NestedSet release evidence."""

import hashlib
import json
import os
import re
import tempfile
import xml.etree.ElementTree as ET
from datetime import date
from pathlib import Path

REPOSITORY = "doka-labs/Doka.EntityFrameworkCore.NestedSet"
PACKAGES = ("Doka.NestedSet", "Doka.EntityFrameworkCore.NestedSet")
STAGES = ("quality", "tests", "packages", "consumer", "sbom")
RELEASE_WORKFLOW = ".github/workflows/release-candidate.yml"


class ReleaseError(ValueError):
    """A named failed gate with an actionable operator response."""

    def __init__(self, code, message, remedy="Inspect the failed gate and retain its evidence before retrying."):
        super().__init__(f"[{code}] {message} Next: {remedy}")
        self.code = code
        self.remedy = remedy


def require(condition, code, message, remedy=None):
    """Reject an unproved release prerequisite instead of inferring success."""
    if not condition:
        raise ReleaseError(code, message, remedy) if remedy else ReleaseError(code, message)


def read_json(path):
    """Read an object while rejecting ambiguous duplicate JSON fields."""
    def unique(pairs):
        result = {}
        for key, value in pairs:
            require(key not in result, "duplicate_json_key", f"Duplicate key {key} in {path}.")
            result[key] = value

        return result

    try:
        return json.loads(Path(path).read_text(encoding="utf-8"), object_pairs_hook=unique)
    except (OSError, json.JSONDecodeError) as error:
        raise ReleaseError("invalid_json", f"Cannot read JSON evidence {path}: {error}") from error


def write_json(path, value):
    """Atomically replace a local receipt so interrupted writes cannot look complete."""
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(mode="w", encoding="ascii", dir=path.parent, delete=False) as stream:
            temporary = Path(stream.name)
            json.dump(value, stream, indent=2, sort_keys=True, ensure_ascii=True)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())

        temporary.replace(path)
    finally:
        if temporary is not None and temporary.exists():
            temporary.unlink()


def sha256(path):
    """Hash file contents in bounded memory."""
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def release_version(value, allow_development=False):
    """Accept canonical stable or numbered RC versions without NuGet normalization aliases."""
    number = r"(?:0|[1-9][0-9]*)"
    pattern = rf"{number}\.{number}\.{number}(?:-rc\.[1-9][0-9]*)?"
    if allow_development:
        pattern = rf"(?:{pattern}|{number}\.{number}\.{number}-dev)"

    require(isinstance(value, str) and re.fullmatch(pattern, value) is not None,
            "invalid_version", "Expected X.Y.Z or X.Y.Z-rc.N with canonical decimal components.",
            "Choose an unused exact version without a leading v or build metadata.")

    return value


def reviewed_release_notes(repo, version):
    """Require the source release line and one dated, nonempty changelog section."""
    release_version(version)
    repo = Path(repo)

    try:
        project = ET.parse(repo / "src/Directory.Build.props")
    except ET.ParseError as error:
        raise ReleaseError("source-version", "Cannot parse the source version properties.") from error

    prefixes = project.findall(".//VersionPrefix")
    require(len(prefixes) == 1 and prefixes[0].text is not None
            and prefixes[0].text.strip() == version.split("-", 1)[0],
            "source-version", f"Source VersionPrefix must match the release line for {version}.")

    changelog = (repo / "CHANGELOG.md").read_text(encoding="ascii")
    headings = list(re.finditer(rf"^## {re.escape(version)}(?:[ \t].*)?$", changelog, re.MULTILINE))
    require(len(headings) == 1, "release-notes", f"CHANGELOG.md needs exactly one section for {version}.")
    heading = headings[0]
    dated = re.fullmatch(rf"## {re.escape(version)} \(([0-9]{{4}}-[0-9]{{2}}-[0-9]{{2}})\)", heading.group())
    require(dated is not None, "release-notes", f"CHANGELOG.md needs a dated section for {version}.")

    try:
        date.fromisoformat(dated.group(1))
    except ValueError as error:
        raise ReleaseError("release-notes", f"CHANGELOG.md has an invalid date for {version}.") from error

    next_heading = re.search(r"^## ", changelog[heading.end():], re.MULTILINE)
    end = heading.end() + next_heading.start() if next_heading else len(changelog)
    body = changelog[heading.end():end].strip()
    require(bool(body), "release-notes", f"CHANGELOG.md needs nonempty notes for {version}.")

    return f"# {version}\n\n{body}\n"


def file_inventory(directory):
    """Inventory regular files, rejecting links and unsupported filesystem entries."""
    directory = Path(directory)
    require(directory.is_dir() and not directory.is_symlink(), "invalid_directory", str(directory))
    result = {}
    for path in sorted(directory.rglob("*")):
        require(not path.is_symlink(), "linked_evidence", f"Symlink is not release evidence: {path}.")
        if path.is_dir():
            continue

        require(path.is_file(), "invalid_evidence", f"Not a regular evidence file: {path}.")
        result[path.relative_to(directory).as_posix()] = {"sha256": sha256(path), "size": path.stat().st_size}

    return result


def load_candidate(candidate, require_publishable=True):
    """Verify the complete candidate file set and its bounded release identity."""
    candidate = Path(candidate)
    value = read_json(candidate / "candidate.json")
    require(isinstance(value, dict) and type(value.get("schemaVersion")) is int and value["schemaVersion"] == 1,
            "candidate_schema", "Unsupported candidate manifest.")
    version = release_version(value.get("version"), allow_development=not require_publishable)
    require(value.get("repository") == REPOSITORY and value.get("tag") == "v" + version,
            "candidate_identity", "Candidate repository or tag does not match the selected version.")
    require(type(value.get("publishable")) is bool, "candidate_identity", "Missing publication classification.")
    require(value.get("stages") == list(STAGES), "incomplete_qualification", "Required stages are missing or reordered.")
    expected = value.get("files")
    require(isinstance(expected, dict) and bool(expected), "candidate_files", "No candidate inventory.")
    actual = file_inventory(candidate)
    actual.pop("candidate.json", None)
    require(actual == expected, "candidate_changed", "Candidate files differ from their sealed manifest.",
            "Restore the original same-run artifact; never rebuild or replace individual files.")
    required = {"package-manifest.json", "qualification-evidence.zip", "release-notes.md"}
    required.update(f"{name}.{version}.{extension}" for name in PACKAGES for extension in ("nupkg", "snupkg"))
    required.update(f"{name}.spdx.json" for name in PACKAGES)
    require(set(expected) == required, "candidate_files", "Unexpected or missing release assets.")
    source = value.get("source", {})
    require(isinstance(source, dict) and isinstance(source.get("fingerprint"), str)
            and re.fullmatch(r"[a-f0-9]{64}", source["fingerprint"]) is not None,
            "source_identity", "Missing source content fingerprint.")
    if require_publishable:
        require(value["publishable"], "workspace_only", "Workspace verification cannot authorize publication.",
                "Qualify a reviewed, clean main commit through the release-candidate workflow.")
        require(all(isinstance(source.get(field), str) and re.fullmatch(r"[a-f0-9]{40}", source[field])
                    for field in ("commit", "tree")),
                "source_identity", "Missing qualified Git commit or tree.")
        workflow = value.get("workflow", {})
        require(isinstance(workflow, dict) and workflow.get("workflow") == RELEASE_WORKFLOW
                and workflow.get("ref") == "refs/heads/main"
                and isinstance(workflow.get("runId"), str) and workflow["runId"].isdigit()
                and int(workflow["runId"]) > 0
                and type(workflow.get("attempt")) is int and workflow["attempt"] > 0,
                "workflow_identity", "Candidate lacks its exact hosted release run and producer attempt.")

    return value

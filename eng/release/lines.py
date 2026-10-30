"""Validate the exact reviewed release lines and each selected source's own toolchain."""

import hashlib
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path

from eng.release.common import PACKAGES, RELEASE_WORKFLOW, REPOSITORY, read_json, release_version, require


ROOT = Path(__file__).resolve().parents[2]
AUTHORITY_REF = "refs/heads/main"
LINE_FIELDS = {"sourceRef", "role", "packageMajor", "channel", "upstreamStage", "state",
               "workflowPath", "publisherEnvironment"}
SNAPSHOT_FIELDS = LINE_FIELDS | {"schemaVersion", "repository", "policySha256", "authorityCommit",
                                "qualificationOnly"}
NUMBER = r"(?:0|[1-9][0-9]*)"
EXACT_VERSION = re.compile(rf"({NUMBER})\.({NUMBER})\.({NUMBER})(?:-(rc\.[1-9][0-9]*(?:\.{NUMBER})*))?\Z")


def _line(value):
    """Validate one exact ref without treating a branch name as a wildcard or a grant."""
    require(isinstance(value, dict) and set(value) == LINE_FIELDS, "line-fields", "Unexpected release-line fields.")
    major = value["packageMajor"]
    role = value["role"]
    require(type(major) is int and major > 0 and role in ("main", "maintenance", "preview"),
            "line-major", "Each release line requires a positive package major and a known role.")
    reference = AUTHORITY_REF if role == "main" else f"refs/heads/{role}/{major}.x"
    require(value["sourceRef"] == reference, "line-ref", "Release-line refs must use their exact reviewed names.")
    require(value["state"] in ("inactive", "prepared", "active"), "line-state", "Unknown release-line state.")
    expected = ("prerelease", "rc") if role == "preview" else ("stable-and-prerelease", "ga")
    require((value["channel"], value["upstreamStage"]) == expected,
            "line-channel", "Preview uses RC-only upstreams; main and maintenance retain their GA graph.")
    require(value["workflowPath"] == RELEASE_WORKFLOW and value["publisherEnvironment"] == "nuget",
            "line-publisher", "Release lines retain the existing workflow and nuget environment.")

    return dict(value)


def validate_policy(value):
    """Require the sole protected-main authority and at most one active publisher per package major."""
    fields = {"schemaVersion", "revision", "repository", "authorityRef", "lines"}
    require(isinstance(value, dict) and set(value) == fields, "line-policy", "Unexpected release-policy fields.")
    require(type(value["schemaVersion"]) is int and value["schemaVersion"] == 1
            and value["repository"] == REPOSITORY and value["authorityRef"] == AUTHORITY_REF,
            "line-policy", "Release authority must be this repository's protected main policy.")
    require(isinstance(value["revision"], str) and re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]*", value["revision"]),
            "line-revision", "Release-policy revision must be a nonempty ASCII identifier.")
    require(isinstance(value["lines"], list) and value["lines"], "line-policy", "No registered release lines.")
    entries = [_line(entry) for entry in value["lines"]]
    refs = [entry["sourceRef"] for entry in entries]
    active = [entry["packageMajor"] for entry in entries if entry["state"] == "active"]
    require(len(refs) == len(set(refs)) and refs.count(AUTHORITY_REF) == 1,
            "line-duplicate", "Register main exactly once and each other exact ref at most once.")
    require(len(active) == len(set(active)), "line-owner", "Only one active publisher may own a package major.")

    return {**value, "lines": entries}


def load_policy(path=None):
    """Read a reviewed local policy; publication independently obtains its current protected-main copy."""
    return validate_policy(read_json(path or ROOT / "eng/release/lines-policy.json"))


def policy_digest(policy):
    """Hash canonical validated policy content independently of JSON formatting or field order."""
    payload = json.dumps(validate_policy(policy), sort_keys=True, separators=(",", ":"), ensure_ascii=True)

    return hashlib.sha256(payload.encode("ascii")).hexdigest()


def _version(line, version):
    """Keep NestedSet version major and publication channel within the selected line."""
    release_version(version)
    require(int(version.split(".", 1)[0]) == line["packageMajor"],
            "line-version", "Package version major differs from its authorized source line.")
    require(line["channel"] != "prerelease" or "-rc." in version,
            "line-version", "A preview source line cannot publish a stable package.")


def select_line(policy, source_ref, version=None, *, purpose="pr"):
    """Select registered PR sources, qualified prepared/active lines, or active publication/maintenance."""
    require(purpose in ("pr", "qualify", "publish", "maintenance"), "line-purpose", "Unknown line selection purpose.")
    entries = validate_policy(policy)["lines"]
    line = next((entry for entry in entries if entry["sourceRef"] == source_ref), None)
    require(line is not None, "line-unknown", "Source ref is not an exact registered release line.")
    required = ("inactive", "prepared", "active") if purpose == "pr" else (
        ("prepared", "active") if purpose == "qualify" else ("active",))
    require(line["state"] in required, "line-inactive", f"The selected line is not authorized for {purpose}.")
    if version is not None:
        _version(line, version)

    return line


def freeze_line(policy, source_ref, version, *, policy_commit, qualification_only=False):
    """Capture one observed authority in the existing candidate identity without creating standing permission."""
    require(type(qualification_only) is bool, "line-mode", "Qualification-only must be Boolean.")
    line = select_line(policy, source_ref, version, purpose="qualify" if qualification_only else "publish")
    snapshot = {**line, "schemaVersion": 1, "repository": REPOSITORY,
                "policySha256": policy_digest(policy), "authorityCommit": policy_commit,
                "qualificationOnly": qualification_only}

    return validate_snapshot(snapshot, source_ref=source_ref, version=version)


def validate_snapshot(snapshot, *, source_ref=None, version=None):
    """Validate frozen historical line identity without requiring today's policy or continued activation."""
    require(isinstance(snapshot, dict) and set(snapshot) == SNAPSHOT_FIELDS,
            "line-snapshot", "Unexpected frozen release-line fields.")
    require(type(snapshot["schemaVersion"]) is int and snapshot["schemaVersion"] == 1
            and snapshot["repository"] == REPOSITORY,
            "line-snapshot", "Frozen release-line repository or schema differs.")
    line = _line({name: snapshot[name] for name in LINE_FIELDS})
    require(line["state"] in ("prepared", "active") and type(snapshot["qualificationOnly"]) is bool,
            "line-snapshot", "Only a prepared or active line can have qualification evidence.")
    require(line["state"] != "prepared" or snapshot["qualificationOnly"],
            "line-mode", "Prepared qualification cannot authorize publication.")
    for name, size in (("authorityCommit", 40), ("policySha256", 64)):
        require(isinstance(snapshot[name], str) and re.fullmatch(rf"[0-9a-f]{{{size}}}", snapshot[name]),
                "line-snapshot", f"Frozen {name} is invalid.")

    require(source_ref is None or source_ref == line["sourceRef"],
            "line-snapshot", "Frozen release line differs from its source ref.")
    if version is not None:
        _version(line, version)

    return dict(snapshot)


def _xml(path):
    """Reject unavailable owned source metadata instead of evaluating an inferred MSBuild graph."""
    try:
        return ET.parse(path)
    except (ET.ParseError, OSError) as error:
        require(False, "line-toolchain", f"Cannot read owned source metadata {path.name}: {error}")


def _property(path, name):
    """Read the single literal property owned by the repository's existing centralized source defaults."""
    document = _xml(path)
    values = document.findall(f".//{name}")
    conditioned = any(group.get("Condition") and group.find(name) is not None
                      for group in document.findall(".//PropertyGroup"))
    require(len(values) == 1 and values[0].text is not None and not values[0].get("Condition") and not conditioned,
            "line-toolchain", f"Expected one unconditional source {name}.")

    return values[0].text.strip()


def _framework_package(name):
    """Recognize framework-coupled dependencies while leaving independent tool/driver majors alone."""
    lowered = name.lower()

    return any(lowered == prefix or lowered.startswith(prefix + ".") for prefix in (
        "microsoft.entityframeworkcore", "doka.entityframeworkcore.mysql",
        "doka.entityframeworkcore.safemigrations", "npgsql.entityframeworkcore.postgresql")) or lowered.startswith(
            "microsoft.extensions.")


def _exact_version(value, major, stage):
    """Require the selected upstream major and GA/RC stage without normalizing source versions."""
    parsed = EXACT_VERSION.fullmatch(value) if isinstance(value, str) else None
    require(parsed is not None and int(parsed[1]) == major,
            "line-toolchain", "Framework dependency or SDK belongs to another major or is not exactly pinned.")
    require((parsed[4] is not None) == (stage == "rc"),
            "line-toolchain", "Framework dependency or SDK does not use the selected GA/RC upstream stage.")

    return tuple(int(parsed[index]) for index in (1, 2, 3))


def _dependency(value, major, stage):
    """Accept exact pins or the existing bounded production compatibility ranges for the selected major."""
    if isinstance(value, str) and value.startswith("["):
        if value.endswith("]") and "," not in value:
            _exact_version(value[1:-1], major, stage)
        else:
            interval = re.fullmatch(r"\[([^,]+),([^,)]+)\)", value)
            require(interval is not None, "line-toolchain", "Framework compatibility ranges must have bounded ends.")
            lower = _exact_version(interval[1].strip(), major, stage)
            upper_value = interval[2].strip()
            upper_match = EXACT_VERSION.fullmatch(upper_value)
            require(upper_match is not None and upper_match[4] is None,
                    "line-toolchain", "Framework compatibility upper bound must be an exclusive GA endpoint.")
            upper = tuple(int(upper_match[index]) for index in (1, 2, 3))
            require((lower < upper or (stage == "rc" and lower == upper)) and upper <= (major + 1, 0, 0),
                    "line-toolchain", "Framework compatibility range crosses the authorized major.")
    else:
        _exact_version(value, major, stage)


def inspect_toolchain(repo, snapshot):
    """Read this exact source's SDK, TFM, central dependencies and committed shipping lock resolutions."""
    line = validate_snapshot(snapshot)
    repo = Path(repo)
    major, stage = line["packageMajor"], line["upstreamStage"]
    global_json = read_json(repo / "global.json")
    require(isinstance(global_json, dict), "line-toolchain", "The source SDK metadata must be an object.")
    sdk = global_json.get("sdk", {})
    require(isinstance(sdk, dict) and sdk.get("rollForward") == "disable"
            and sdk.get("allowPrerelease") is (stage == "rc"),
            "line-toolchain", "The source SDK must be fixed and explicitly select its GA/RC stage.")
    _exact_version(sdk.get("version"), major, stage)
    framework = _property(repo / "Directory.Build.props", "TargetFramework")
    require(framework == f"net{major}.0", "line-toolchain", "Source TFM differs from its authorized framework major.")
    prefix = _property(repo / "src/Directory.Build.props", "VersionPrefix")
    release_version(prefix)
    require("-" not in prefix and int(prefix.split(".", 1)[0]) == major,
            "line-toolchain", "Source package prefix differs from its authorized framework major.")
    versions = []
    for entry in _xml(repo / "Directory.Packages.props").findall(".//PackageVersion"):
        name = entry.get("Include") or entry.get("Update")
        if isinstance(name, str) and _framework_package(name):
            _dependency(entry.get("Version"), major, stage)
            versions.append({"id": name, "version": entry.get("Version")})

    require(any(entry["id"].lower() == "microsoft.entityframeworkcore" for entry in versions),
            "line-toolchain", "The source has no explicit EF Core baseline.")
    for package in PACKAGES:
        path = repo / "src" / package / "packages.lock.json"
        if not path.exists():
            # WHY: Source preparation fixtures may not have restored yet. The qualifier separately owns
            # shipping-lock completeness; any checked-in resolution present here must already fit its line.
            continue

        locked = read_json(path)
        require(isinstance(locked, dict), "line-toolchain", "Shipping lock must contain a JSON object.")
        targets = locked.get("dependencies")
        require(isinstance(targets, dict) and targets, "line-toolchain", "Shipping lock lacks its framework graph.")
        for target, dependencies in targets.items():
            require(target.split("/", 1)[0] == framework and isinstance(dependencies, dict),
                    "line-toolchain", "Shipping lock belongs to another target framework.")
            for name, dependency in dependencies.items():
                if _framework_package(name):
                    require(isinstance(dependency, dict), "line-toolchain", "Invalid locked framework dependency.")
                    _exact_version(dependency.get("resolved"), major, stage)

    return {"sdkVersion": sdk["version"], "targetFramework": framework, "frameworkMajor": major,
            "upstreamStage": stage, "packageVersions": versions}

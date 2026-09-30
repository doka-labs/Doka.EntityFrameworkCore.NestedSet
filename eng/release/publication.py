"""Verify one hosted candidate, stage its exact assets, and finish publication.

Only ``stage`` and ``publish-release`` write remote state. Package publication
belongs to the protected workflow; this module never obtains NuGet credentials.
"""

import argparse
import base64
import binascii
import json
import os
import re
import subprocess
import tempfile
import time
from pathlib import Path

from eng.release.common import (
    PACKAGES,
    REPOSITORY,
    ReleaseError,
    file_inventory,
    load_candidate,
    read_json,
    release_version,
    require,
    reviewed_release_notes,
    sha256,
    write_json,
)

WORKFLOW = ".github/workflows/release-candidate.yml"
API_VERSION = "2026-03-10"
SLSA_TYPE = "https://slsa.dev/provenance/v1"
# The pinned actions/attest implementation derives this suffix from the document's SPDX-2.2 version.
SPDX_TYPE = "https://spdx.dev/Document/v2.2"
PROVENANCE_FILES = {"release-provenance.jsonl", *(f"sbom-{package}.jsonl" for package in PACKAGES)}
ROOT = Path(__file__).resolve().parents[2]


class Commands:
    """Keep subprocess execution injectable for offline trust-boundary fixtures."""

    def run(self, arguments):
        """Execute a bounded command without a shell or disclosing credential values."""
        try:
            # WHY: Inspection commands must not refresh Git index metadata as a side effect.
            result = subprocess.run(arguments, cwd=ROOT, capture_output=True, check=False, timeout=180,
                                    env={**os.environ, "GIT_OPTIONAL_LOCKS": "0"})
        except (OSError, subprocess.TimeoutExpired) as error:
            raise ReleaseError("command-unavailable", f"Cannot complete {arguments[0]} command: {error}") from error

        detail = result.stderr.decode("utf-8", errors="replace").strip()
        require(result.returncode == 0, "command-failed",
                f"{arguments[0]} command failed (exit {result.returncode}): {detail}",
                "Inspect the failed hosted step and its command; do not bypass this check.")

        return result.stdout.decode("utf-8")


def version_tag(version):
    """Accept canonical stable or prerelease NuGet versions without normalization."""
    release_version(version)

    return f"v{version}"


def parse_json(text, context):
    """Reject malformed or duplicate JSON keys at an external evidence boundary."""
    def unique_pairs(pairs):
        result = {}

        for key, value in pairs:
            require(key not in result, "json-duplicate", f"Duplicate JSON key in {context}: {key}")
            result[key] = value

        return result

    try:
        return json.loads(text, object_pairs_hook=unique_pairs)
    except (ValueError, UnicodeError) as error:
        raise ReleaseError("json-invalid", f"Invalid JSON in {context}.") from error


def api(commands, path, *, paginate=False):
    """Read authenticated GitHub metadata, including every page when requested."""
    arguments = ["gh", "api", "--method", "GET", "-H", "Accept: application/vnd.github+json",
                 "-H", f"X-GitHub-Api-Version: {API_VERSION}"]

    if paginate:
        arguments.extend(["--paginate", "--slurp"])

    payload = parse_json(commands.run([*arguments, f"repos/{REPOSITORY}/{path}".rstrip("/")]), path)

    if paginate:
        require(isinstance(payload, list) and all(isinstance(page, list) for page in payload),
                "api-shape", f"Expected paginated arrays from {path}.")
        require(all(isinstance(entry, dict) for page in payload for entry in page),
                "api-shape", f"Expected paginated objects from {path}.")

        return [entry for page in payload for entry in page]

    require(isinstance(payload, dict), "api-shape", f"Expected an object from {path}.")

    return payload


def remote_main(commands):
    """Read origin's current main commit without fetching or changing local refs."""
    remote = commands.run(["git", "remote", "get-url", "origin"]).strip()
    require(remote in (f"https://github.com/{REPOSITORY}", f"https://github.com/{REPOSITORY}.git",
                       f"git@github.com:{REPOSITORY}.git"),
            "remote-mismatch", "Origin is not the canonical repository.")
    rows = commands.run(["git", "ls-remote", "--refs", "origin", "refs/heads/main"]).splitlines()
    require(len(rows) == 1, "main-missing", "Expected exactly one remote main ref.")
    values = rows[0].split()
    require(len(values) == 2 and re.fullmatch("[0-9a-f]{40}", values[0]) is not None
            and values[1] == "refs/heads/main", "main-invalid", "Remote main has an invalid identity.")

    return values[0]


def hosted_identity(manifest, environment):
    """Bind the candidate to this repository, workflow, source, and same-run retry."""
    source = manifest["source"]["commit"]
    producer = manifest["workflow"]
    expected = {"GITHUB_ACTIONS": "true", "GITHUB_REPOSITORY": REPOSITORY,
                "GITHUB_REF": "refs/heads/main", "GITHUB_SHA": source,
                "GITHUB_WORKFLOW_REF": f"{REPOSITORY}/{WORKFLOW}@refs/heads/main",
                "GITHUB_EVENT_NAME": "workflow_dispatch", "GITHUB_RUN_ID": str(producer["runId"])}

    for name, value in expected.items():
        require(environment.get(name) == value, "hosted-identity", f"{name} does not match the qualified candidate.")

    attempt = environment.get("GITHUB_RUN_ATTEMPT", "")
    require(attempt.isdecimal() and int(attempt) >= producer["attempt"] > 0,
            "attempt-mismatch", "Publication must use the producing run and its current or later attempt.")
    require(producer.get("ref") == "refs/heads/main" and producer.get("workflow") == WORKFLOW,
            "producer-mismatch", "Candidate producer is not the release-candidate workflow on main.")

    return int(attempt)


def statements(path):
    """Decode signed Sigstore DSSE statements before cryptographic verification."""
    records = path.read_text(encoding="utf-8").splitlines()
    require(len(records) == 1 and records[0].strip(), "bundle-count", f"Expected one signed bundle in {path.name}.")
    bundle = parse_json(records[0], path.name)
    require(isinstance(bundle, dict), "bundle-shape", "Expected a Sigstore bundle object.")
    envelope = bundle.get("dsseEnvelope", {})
    require(isinstance(envelope, dict), "bundle-shape", "Expected a DSSE envelope object.")
    signatures = envelope.get("signatures", [])
    require(envelope.get("payloadType") == "application/vnd.in-toto+json"
            and isinstance(signatures, list) and signatures
            and all(isinstance(item, dict) and isinstance(item.get("sig"), str) and item["sig"] for item in signatures),
            "bundle-signature", "Expected an in-toto DSSE envelope with signatures.")

    try:
        decoded = base64.b64decode(envelope.get("payload", ""), validate=True).decode("utf-8")
    except (ValueError, UnicodeError, binascii.Error) as error:
        raise ReleaseError("bundle-payload", "Invalid base64 DSSE payload.") from error

    statement = parse_json(decoded, path.name)
    require(isinstance(statement, dict) and statement.get("_type") == "https://in-toto.io/Statement/v1",
            "statement-type", "Expected an in-toto v1 statement.")
    require(isinstance(statement.get("predicate"), dict), "predicate-shape", "Expected a predicate object.")

    return statement


def verify_subjects(statement, expected):
    """Require a complete, unique subject set with exact candidate SHA-256 hashes."""
    subjects = statement.get("subject", [])
    require(isinstance(subjects, list) and all(isinstance(subject, dict) for subject in subjects),
            "subjects-invalid", "Expected attestation subjects.")
    names = [subject.get("name") for subject in subjects]
    require(all(isinstance(name, str) for name in names)
            and len(names) == len(set(names)) and set(names) == set(expected),
            "subjects-mismatch", "Attestation subject inventory differs from candidate.")

    for subject in subjects:
        require(subject.get("digest") == {"sha256": expected[subject["name"]]},
                "subject-digest", f"Attestation hash differs for {subject['name']}.")


def verify_origin(statement, manifest, current_attempt):
    """Bind signed provenance to its source workflow and exact producing run."""
    require(statement.get("predicateType") == SLSA_TYPE, "predicate-type", "Expected SLSA v1 provenance.")
    predicate = statement.get("predicate", {})
    definition = predicate.get("buildDefinition", {})
    require(isinstance(definition, dict) and isinstance(definition.get("externalParameters"), dict),
            "provenance-shape", "Expected a SLSA workflow build definition.")
    workflow = definition["externalParameters"].get("workflow", {})
    require(isinstance(workflow, dict), "provenance-shape", "Expected the producing workflow identity.")
    require(definition.get("buildType") == "https://actions.github.io/buildtypes/workflow/v1"
            and workflow.get("repository") == f"https://github.com/{REPOSITORY}"
            and workflow.get("path") == WORKFLOW and workflow.get("ref") == "refs/heads/main",
            "provenance-workflow", "Provenance is not from the configured workflow on main.")
    dependencies = definition.get("resolvedDependencies", [])
    require(isinstance(dependencies, list), "provenance-shape", "Expected resolved source dependencies.")
    source_uri = f"git+https://github.com/{REPOSITORY}@refs/heads/main"
    matching = [item for item in dependencies if isinstance(item, dict) and item.get("uri") == source_uri]
    require(len(matching) == 1 and matching[0].get("digest") == {"gitCommit": manifest["source"]["commit"]},
            "provenance-source", "Provenance source commit differs from the candidate.")
    details = predicate.get("runDetails", {})
    require(isinstance(details, dict) and isinstance(details.get("metadata"), dict),
            "provenance-shape", "Expected the producing invocation identity.")
    invocation = details["metadata"].get("invocationId", "")
    require(isinstance(invocation, str), "provenance-run", "Expected a producing invocation URL.")
    prefix = f"https://github.com/{REPOSITORY}/actions/runs/{manifest['workflow']['runId']}/attempts/"
    attempt = invocation.removeprefix(prefix)
    require(invocation.startswith(prefix) and attempt.isdecimal()
            and manifest["workflow"]["attempt"] <= int(attempt) <= current_attempt,
            "provenance-run", "Provenance is not from this run's qualified or later attestation attempt.")


def cryptographic_verify(commands, subject, bundle, manifest, predicate_type):
    """Ask GitHub CLI to enforce signatures, issuer, workflow, source, and hosted runner policy."""
    arguments = ["gh", "attestation", "verify", str(subject), "--bundle", str(bundle), "--repo", REPOSITORY,
                 "--signer-workflow", f"{REPOSITORY}/{WORKFLOW}", "--signer-digest", manifest["source"]["commit"],
                 "--source-ref", "refs/heads/main", "--source-digest", manifest["source"]["commit"],
                 "--deny-self-hosted-runners", "--predicate-type", predicate_type, "--format", "json"]
    result = parse_json(commands.run(arguments), "gh attestation verify")
    require(isinstance(result, list) and result, "verification-empty", "Cryptographic verification returned no result.")


def attest_verify(candidate, provenance, *, commands=None, environment=None):
    """Verify candidate bytes and every portable provenance/SBOM signature before writes."""
    commands = commands or Commands()
    environment = os.environ if environment is None else environment
    manifest = load_candidate(candidate)
    current_attempt = hosted_identity(manifest, environment)
    require(provenance.is_dir() and {path.name for path in provenance.iterdir()} == PROVENANCE_FILES
            and all(path.is_file() and not path.is_symlink() for path in provenance.iterdir()),
            "provenance-inventory", "Portable provenance inventory is missing, extra, or nonregular.")
    original_candidate = file_inventory(candidate)
    original_provenance = file_inventory(provenance)
    subjects = {"candidate.json": sha256(candidate / "candidate.json")}

    for package in PACKAGES:
        for extension in ("nupkg", "snupkg"):
            name = f"{package}.{manifest['version']}.{extension}"
            subjects[name] = manifest["files"][name]["sha256"]

    bundle = provenance / "release-provenance.jsonl"
    statement = statements(bundle)
    verify_subjects(statement, subjects)
    verify_origin(statement, manifest, current_attempt)

    for name in subjects:
        cryptographic_verify(commands, candidate / name, bundle, manifest, SLSA_TYPE)

    for package in PACKAGES:
        bundle = provenance / f"sbom-{package}.jsonl"
        name = f"{package}.{manifest['version']}.nupkg"
        statement = statements(bundle)
        verify_subjects(statement, {name: subjects[name]})
        require(statement.get("predicateType") == SPDX_TYPE
                and statement.get("predicate") == read_json(candidate / f"{package}.spdx.json")
                and statement["predicate"].get("spdxVersion") == "SPDX-2.2",
                "sbom-predicate", f"Signed SPDX predicate differs from {package}'s qualified SBOM.")
        cryptographic_verify(commands, candidate / name, bundle, manifest, SPDX_TYPE)

    require(file_inventory(candidate) == original_candidate and file_inventory(provenance) == original_provenance,
            "evidence-changed", "Candidate or provenance changed during cryptographic verification.")

    return manifest


def verify_repository(commands):
    """Read actual repository/main protection rather than inferring hosted setup from YAML."""
    # WHY: The immutable-releases settings endpoint requires Administration(read), which the job token lacks.
    # Operators check that setting before dispatch; finalization independently requires an immutable release.
    repository = api(commands, "")
    require(repository.get("full_name") == REPOSITORY and repository.get("private") is False
            and repository.get("archived") is False,
            "repository-policy", "Expected the active public NestedSet repository.")
    main = api(commands, "branches/main")
    require(main.get("protected") is True, "main-unprotected", "Main must have active branch protection or rulesets.")


def verify_run(commands, manifest, environment):
    """Cross-check hosted variables with GitHub's authenticated run-attempt metadata."""
    run = manifest["workflow"]["runId"]
    attempt = environment["GITHUB_RUN_ATTEMPT"]
    metadata = api(commands, f"actions/runs/{run}/attempts/{attempt}")
    require(str(metadata.get("id")) == run and metadata.get("run_attempt") == int(attempt)
            and metadata.get("path") == WORKFLOW and metadata.get("head_sha") == manifest["source"]["commit"]
            and metadata.get("head_branch") == "main" and metadata.get("event") == "workflow_dispatch"
            and metadata.get("repository", {}).get("full_name") == REPOSITORY,
            "run-mismatch", "GitHub run-attempt metadata differs from this candidate's source and workflow.")


def verify_signed_tag(commands, manifest, environment):
    """Verify the exact annotated SSH tag locally and GitHub's independent signature verdict."""
    tag = manifest["tag"]
    commit = manifest["source"]["commit"]
    require(tag == version_tag(manifest["version"]), "tag-mismatch", "Candidate tag does not match its version.")
    require(commands.run(["git", "rev-parse", "HEAD"]).strip() == commit,
            "checkout-mismatch", "Checkout differs from the qualified source.")
    require(commands.run(["git", "rev-parse", "HEAD^{tree}"]).strip() == manifest["source"]["tree"],
            "checkout-tree", "Checkout tree differs from the qualified source tree.")
    require(not commands.run(["git", "status", "--porcelain"]).strip(),
            "source-dirty", "Publication must execute the unchanged qualified checkout.")
    current_main = remote_main(commands)
    commands.run(["git", "merge-base", "--is-ancestor", commit, current_main])
    require(commands.run(["git", "cat-file", "-t", f"refs/tags/{tag}"]).strip() == "tag",
            "tag-lightweight", "Release identity requires an annotated tag.")
    require(commands.run(["git", "rev-parse", f"refs/tags/{tag}^{{commit}}"]).strip() == commit,
            "tag-commit", "Signed release tag does not resolve to the qualified commit.")
    raw_tag = commands.run(["git", "cat-file", "tag", f"refs/tags/{tag}"])
    require("-----BEGIN SSH SIGNATURE-----" in raw_tag, "tag-signature", "Expected an SSH-signed annotated tag.")
    signers = environment.get("RELEASE_ALLOWED_SIGNERS", "")
    require(signers.strip(), "signer-policy-missing",
            "RELEASE_ALLOWED_SIGNERS must contain the approved OpenSSH policy.")

    # WHY: The policy is maintained outside the candidate so candidate code cannot authorize its own tag signer.
    with tempfile.TemporaryDirectory(prefix="nestedset-tag-policy-") as temporary:
        policy = Path(temporary) / "allowed_signers"
        policy.write_text(signers, encoding="utf-8")
        policy.chmod(0o600)
        commands.run(["git", "-c", "gpg.format=ssh", "-c", f"gpg.ssh.allowedSignersFile={policy}", "verify-tag", tag])

    local_tag = commands.run(["git", "rev-parse", f"refs/tags/{tag}"]).strip()
    reference = api(commands, f"git/ref/tags/{tag}")
    target = reference.get("object", {})
    require(target.get("type") == "tag" and target.get("sha") == local_tag,
            "remote-tag-mismatch", "Remote annotated tag differs from the locally verified tag object.")
    remote_tag = api(commands, f"git/tags/{local_tag}")
    verification = remote_tag.get("verification", {})
    require(remote_tag.get("tag") == tag and remote_tag.get("object", {}).get("type") == "commit"
            and remote_tag.get("object", {}).get("sha") == commit
            and verification.get("verified") is True and verification.get("reason") == "valid",
            "remote-tag-signature", "GitHub has not verified the expected commit's annotated tag signature.")


def authorize(candidate, provenance, *, commands=None, environment=None):
    """Recheck all origin and signer guards; this function performs no remote writes."""
    commands = commands or Commands()
    environment = os.environ if environment is None else environment
    original_candidate = file_inventory(candidate)
    original_provenance = file_inventory(provenance)
    manifest = attest_verify(candidate, provenance, commands=commands, environment=environment)
    verify_repository(commands)
    verify_run(commands, manifest, environment)
    verify_signed_tag(commands, manifest, environment)
    require(file_inventory(candidate) == original_candidate and file_inventory(provenance) == original_provenance,
            "evidence-changed", "Candidate or provenance changed during authorization.")

    return manifest


def expected_assets(candidate, provenance):
    """Bind every release asset, including candidate metadata and portable bundles."""
    paths = [*candidate.iterdir(), *provenance.iterdir()]
    require(len({path.name for path in paths}) == len(paths), "asset-collision", "Candidate/provenance names collide.")

    return {path.name: {"path": path, "sha256": sha256(path), "size": path.stat().st_size} for path in paths}


def release_state(commands, manifest):
    """Find zero or one release across authenticated pages, including drafts."""
    releases = api(commands, "releases?per_page=100", paginate=True)
    matches = [release for release in releases if release.get("tag_name") == manifest["tag"]]
    require(len(matches) <= 1, "release-duplicate", "Multiple releases claim this candidate tag.")

    return matches[0] if matches else None


def validate_release(commands, release, manifest, candidate, assets, *, allow_missing, allow_transition=False):
    """Reject metadata/asset conflicts; incomplete matching uploads can be resumed."""
    require(release.get("name") == manifest["tag"] and release.get("tag_name") == manifest["tag"]
            and release.get("body") == (candidate / "release-notes.md").read_text(encoding="utf-8")
            and release.get("prerelease") is ("-" in manifest["version"]),
            "release-metadata", "Release tag, title, notes, or prerelease classification conflicts with the candidate.")
    require(release.get("draft") is True or (release.get("draft") is False
            and (release.get("immutable") is True or allow_transition)),
            "release-mutable", "An existing published release must already be immutable.")
    entries = api(commands, f"releases/{release['id']}/assets?per_page=100", paginate=True)
    names = [entry.get("name") for entry in entries]
    require(all(isinstance(name, str) for name in names) and len(names) == len(set(names))
            and set(names) <= set(assets), "release-assets", "Duplicate or unexpected release assets exist.")
    pending = []

    for entry in entries:
        expected = assets[entry["name"]]
        require(entry.get("size") == expected["size"], "asset-size", f"Remote size changed: {entry['name']}.")
        digest = entry.get("digest")
        require(digest is None or digest == f"sha256:{expected['sha256']}",
                "asset-digest", f"Remote hash changed: {entry['name']}.")

        if digest is None or entry.get("state") != "uploaded":
            pending.append(entry["name"])

    missing = sorted(set(assets) - set(names))
    require(allow_missing or not missing, "release-incomplete",
            "Published release is missing required candidate assets.")

    return missing, pending


def wait_assets(commands, manifest, candidate, assets, *, published=False, attempts=30, sleep=time.sleep):
    """Wait only for missing metadata; conflicting remote bytes are immediately terminal."""
    for attempt in range(attempts):
        release = release_state(commands, manifest)

        if release is not None:
            missing, pending = validate_release(commands, release, manifest, candidate, assets,
                                                allow_missing=True, allow_transition=published)

            if not missing and not pending and (not published or
                                                (release.get("draft") is False and release.get("immutable") is True)):
                return release

        if attempt + 1 < attempts:
            sleep(10)

    raise ReleaseError("release-timeout", "Release metadata did not converge within the bounded readback window.",
                       "Retry only publication in the same run with the retained exact candidate.")


def receipt_location(output, candidate, provenance):
    """Keep mutable attempt receipts outside the sealed candidate and provenance inventories."""
    path = Path(output).resolve()
    require(not path.is_relative_to(candidate.resolve()) and not path.is_relative_to(provenance.resolve()),
            "receipt-location", "Write publication observations outside the retained immutable inputs.")


def receipt(manifest, candidate, provenance, *, state, release=None, environment=None):
    """Capture immutable inputs and observed release identity without modifying its assets."""
    result = {"schemaVersion": 1, "repository": REPOSITORY, "version": manifest["version"], "tag": manifest["tag"],
              "source": manifest["source"], "workflow": manifest["workflow"], "state": state,
              "candidateSha256": sha256(candidate / "candidate.json"),
              "assets": {name: {"sha256": item["sha256"], "size": item["size"]}
                         for name, item in expected_assets(candidate, provenance).items()}}
    current = os.environ if environment is None else environment
    result["observation"] = {"runId": current.get("GITHUB_RUN_ID"), "attempt": current.get("GITHUB_RUN_ATTEMPT")}

    if release is not None:
        result["release"] = {"id": release["id"], "url": release.get("html_url"),
                             "draft": release["draft"], "immutable": release.get("immutable", False)}

    return result


def stage(candidate, provenance, output, *, commands=None, environment=None, attempts=30, sleep=time.sleep):
    """Stage exact matching assets before credentials; never replace an existing asset."""
    receipt_location(output, candidate, provenance)
    commands = commands or Commands()
    manifest = authorize(candidate, provenance, commands=commands, environment=environment)
    assets = expected_assets(candidate, provenance)
    release = release_state(commands, manifest)

    if release is None:
        arguments = ["gh", "release", "create", manifest["tag"], "--repo", REPOSITORY, "--draft", "--verify-tag",
                     "--title", manifest["tag"], "--notes-file", str(candidate / "release-notes.md")]

        if "-" in manifest["version"]:
            arguments.append("--prerelease")

        commands.run(arguments)

        for attempt in range(attempts):
            release = release_state(commands, manifest)

            if release is not None:
                break

            if attempt + 1 < attempts:
                sleep(10)

        require(release is not None, "release-create-readback", "Created draft is not visible; retry the same run.")

    missing, _ = validate_release(commands, release, manifest, candidate, assets, allow_missing=release["draft"])

    for name in missing:
        require(sha256(assets[name]["path"]) == assets[name]["sha256"],
                "evidence-changed", f"Asset changed before upload: {name}.")
        commands.run(["gh", "release", "upload", manifest["tag"], str(assets[name]["path"]), "--repo", REPOSITORY])

    release = wait_assets(commands, manifest, candidate, assets, attempts=attempts, sleep=sleep)
    require(expected_assets(candidate, provenance) == assets,
            "evidence-changed", "Candidate or provenance changed during staging.")
    result = receipt(manifest, candidate, provenance, state="staged", release=release, environment=environment)
    write_json(output, result)

    return result


def verify_readback(readback, manifest, candidate):
    """Require exact package/signature/symbol completion before publishing the draft."""
    observed = read_json(readback)
    require(observed.get("schemaVersion") == 1 and observed.get("repository") == REPOSITORY
            and observed.get("version") == manifest["version"]
            and observed.get("success") is True, "nuget-readback", "NuGet readback is missing or incomplete.")
    packages = read_json(candidate / "package-manifest.json")["packages"]
    actual = observed.get("packages", [])
    require([item.get("id") for item in actual] == list(PACKAGES),
            "nuget-package-inventory", "NuGet readback must cover Core and then EF.")

    for expected, item in zip(packages, actual, strict=True):
        expected_symbols = [{**symbol, "verified": True} for symbol in expected["symbols"]]
        require(item.get("version") == manifest["version"] and item.get("nupkgSha256") == expected["nupkgSha256"]
                and item.get("snupkgSha256") == expected["snupkgSha256"] and item.get("signatureVerified") is True
                and item.get("symbols") == expected_symbols
                and all(re.fullmatch("[0-9a-f]{64}", item.get(field, "")) is not None
                        for field in ("publicNupkgSha256", "canonicalSha256")),
                "nuget-package-readback", f"Incomplete public package/symbol readback for {expected['id']}.")

    return observed


def verify_immutable(commands, manifest, candidate, assets, *, attempts=30, sleep=time.sleep):
    """Verify GitHub's immutable-release attestation and each exact local asset."""
    require(attempts > 0, "release-retries", "Immutable release verification needs a positive retry budget.")
    release = wait_assets(commands, manifest, candidate, assets, published=True, attempts=attempts, sleep=sleep)
    verification_commands = [["gh", "release", "verify", manifest["tag"], "--repo", REPOSITORY, "--format", "json"]]
    verification_commands.extend(["gh", "release", "verify-asset", manifest["tag"], str(asset["path"]),
                                  "--repo", REPOSITORY, "--format", "json"] for asset in assets.values())

    # WHY: GitHub emits immutable-release attestations asynchronously after the draft is published.
    # Never reinterpret a failed verification as success; only a later successful verification can finish.
    retries_left = attempts - 1

    for arguments in verification_commands:
        while True:
            try:
                commands.run(arguments)
                break
            except ReleaseError as error:
                if error.code != "command-failed" or retries_left == 0:
                    raise

                # WHY: Per-asset retry counters multiply the publication window as asset count grows.
                retries_left -= 1
                sleep(10)

    if "-" not in manifest["version"]:
        latest = api(commands, "releases/latest")
        require(latest.get("id") == release["id"], "release-latest", "Stable release is not GitHub's latest release.")

    return release


def publish_release(candidate, provenance, readback, output, *, commands=None, environment=None,
                    attempts=30, sleep=time.sleep):
    """Publish only a complete matching draft after signed NuGet and symbol readback."""
    receipt_location(output, candidate, provenance)
    commands = commands or Commands()
    manifest = authorize(candidate, provenance, commands=commands, environment=environment)
    verify_readback(readback, manifest, candidate)
    assets = expected_assets(candidate, provenance)
    release = release_state(commands, manifest)
    require(release is not None, "draft-missing", "The candidate must be staged before publication.")
    missing, pending = validate_release(commands, release, manifest, candidate, assets, allow_missing=False)
    require(not missing and not pending, "draft-incomplete",
            "The draft must have complete verified assets before publication.")
    require(expected_assets(candidate, provenance) == assets,
            "evidence-changed", "Candidate or provenance changed before draft publication.")

    if release["draft"]:
        prerelease = "true" if "-" in manifest["version"] else "false"
        latest = "false" if prerelease == "true" else "true"
        commands.run(["gh", "release", "edit", manifest["tag"], "--repo", REPOSITORY, "--draft=false",
                      f"--prerelease={prerelease}", f"--latest={latest}"])

    release = verify_immutable(commands, manifest, candidate, assets, attempts=attempts, sleep=sleep)
    result = receipt(manifest, candidate, provenance, state="published", release=release, environment=environment)
    result["nugetReadbackSha256"] = sha256(readback)
    write_json(output, result)

    return result


def complete(candidate, provenance, readback, output, *, commands=None, environment=None,
             attempts=30, sleep=time.sleep):
    """Recheck all trust boundaries and retain a completion receipt; no remote writes."""
    receipt_location(output, candidate, provenance)
    commands = commands or Commands()
    manifest = authorize(candidate, provenance, commands=commands, environment=environment)
    observed = verify_readback(readback, manifest, candidate)
    release = verify_immutable(commands, manifest, candidate, expected_assets(candidate, provenance),
                               attempts=attempts, sleep=sleep)
    result = receipt(manifest, candidate, provenance, state="complete", release=release, environment=environment)
    result["nugetReadbackSha256"] = sha256(readback)
    result["nuget"] = observed
    write_json(output, result)

    return result


def pre_tag(version=None, candidate=None, *, commands=None, source_repo=ROOT):
    """Inspect local/remote source and SSH signing configuration without Git mutations."""
    commands = commands or Commands()

    if version is not None:
        reviewed_release_notes(source_repo, version)

    commit = commands.run(["git", "rev-parse", "HEAD"]).strip()
    require(not commands.run(["git", "status", "--porcelain", "--untracked-files=all"]).strip(),
            "source-dirty", "Prepare a clean checkout first.")
    require(commands.run(["git", "branch", "--show-current"]).strip() == "main",
            "source-branch", "Pre-tag inspection requires the main branch.")
    require(remote_main(commands) == commit, "source-stale", "Checkout is not current remote main.")

    # WHY: Remote peeled tags identify already tagged commits without fetching or trusting stale local tags.
    remote_tags = commands.run(["git", "ls-remote", "--tags", "origin"])
    tag_pattern = r"refs/tags/v[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?(?:\^\{\})?"
    tagged = any(value == commit and re.fullmatch(tag_pattern, reference) is not None
                 for value, reference in (line.split() for line in remote_tags.splitlines()))
    require(not tagged, "tag-exists", "A semantic release tag already identifies this source commit.")

    if version is not None:
        tag = version_tag(version)
        require(not commands.run(["git", "ls-remote", "--refs", "origin", f"refs/tags/{tag}"]).strip(),
                "tag-exists", "The release tag already exists; use the same-run recovery procedure.")

    require(commands.run(["git", "config", "--get", "gpg.format"]).strip() == "ssh",
            "signing-format", "Configure SSH signing before creating the release tag.")
    signing_key = commands.run(["git", "config", "--get", "user.signingkey"]).strip()
    require(signing_key,
            "signing-key", "Configure the approved signing key before creating the release tag.")
    require(signing_key.startswith(("key::", "ssh-")) or Path(signing_key).expanduser().is_file(),
            "signing-key", "The configured signing key file does not exist.")

    if candidate is not None:
        manifest = load_candidate(candidate)
        require(manifest["source"]["commit"] == commit and manifest["version"] == version,
                "candidate-source", "Candidate does not describe this source and release version.")

    result = {"schemaVersion": 1, "repository": REPOSITORY,
              "sourceCommit": commit, "state": "pre-tag-checked"}

    if version is not None:
        result.update(version=version, tag=tag)

    return result


def preflight(version, *, commands=None, environment=None, source_repo=ROOT):
    """Check hosted current main and unused tag before reversible qualification."""
    commands = commands or Commands()
    reviewed_release_notes(source_repo, version)
    environment = os.environ if environment is None else environment
    tag = version_tag(version)
    commit = commands.run(["git", "rev-parse", "HEAD"]).strip()
    attempt = environment.get("GITHUB_RUN_ATTEMPT", "")
    require(attempt.isdecimal() and int(attempt) > 0, "attempt-invalid", "Expected a hosted workflow attempt.")
    manifest = {"source": {"commit": commit}, "workflow": {"runId": environment.get("GITHUB_RUN_ID", ""),
                "attempt": int(attempt), "ref": "refs/heads/main", "workflow": WORKFLOW}}
    hosted_identity(manifest, environment)
    require(not commands.run(["git", "status", "--porcelain"]).strip(), "source-dirty", "Hosted source must be clean.")
    require(remote_main(commands) == commit, "source-stale", "Dispatch must qualify current remote main.")
    require(not commands.run(["git", "ls-remote", "--refs", "origin", f"refs/tags/{tag}"]).strip(),
            "tag-exists", "A release tag already exists; do not build a replacement candidate.")
    verify_repository(commands)
    verify_run(commands, manifest, environment)

    from eng.release.nuget import check_unused_version

    availability = check_unused_version(version)

    return {"schemaVersion": 1, "repository": REPOSITORY, "version": version, "tag": tag,
            "sourceCommit": commit, "workflow": manifest["workflow"], "state": "preflight-checked",
            "nuget": availability}


def main():
    """Expose one explicit command per operator/workflow publication boundary."""
    parser = argparse.ArgumentParser(description=__doc__)
    subcommands = parser.add_subparsers(dest="command", required=True)

    for name in ("preflight", "pre-tag"):
        command = subcommands.add_parser(name)
        command.add_argument("--version", required=name == "preflight")
        command.add_argument("--output", type=Path)

        if name == "pre-tag":
            command.add_argument("--candidate", type=Path)

    for name in ("attest-verify", "authorize", "stage", "publish-release", "complete"):
        command = subcommands.add_parser(name)
        command.add_argument("--candidate", type=Path, required=True)
        command.add_argument("--provenance", type=Path, required=True)
        command.add_argument("--output", type=Path, required=name in ("stage", "publish-release", "complete"))

        if name in ("publish-release", "complete"):
            command.add_argument("--readback", type=Path, required=True)

    arguments = parser.parse_args()

    try:
        if arguments.command == "preflight":
            result = preflight(arguments.version)
        elif arguments.command == "pre-tag":
            result = pre_tag(arguments.version, arguments.candidate)
        elif arguments.command in ("attest-verify", "authorize"):
            action = attest_verify if arguments.command == "attest-verify" else authorize
            manifest = action(arguments.candidate, arguments.provenance)
            result = receipt(manifest, arguments.candidate, arguments.provenance, state=arguments.command)
        elif arguments.command == "stage":
            result = stage(arguments.candidate, arguments.provenance, arguments.output)
        else:
            action = publish_release if arguments.command == "publish-release" else complete
            result = action(arguments.candidate, arguments.provenance, arguments.readback, arguments.output)

        if arguments.output is not None:
            write_json(arguments.output, result)

        if arguments.command == "pre-tag":
            print(f"Ready to start Release candidate for {result['sourceCommit']}.")
            print("Create the signed tag only after qualification and attestations succeed.")
        else:
            print(json.dumps(result, sort_keys=True, indent=2))
    except (ValueError, OSError, KeyError, TypeError) as error:
        parser.exit(1, f"Release check failed: {error}\n")


if __name__ == "__main__":
    main()

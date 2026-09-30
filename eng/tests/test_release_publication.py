"""Offline adversarial probes for publication identity, ordering, and recovery."""

import base64
import copy
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from eng.release import publication as release
from eng.release.common import STAGES, file_inventory, sha256, write_json


def record_error(action):
    """Separate the single operation under test from its failure assertions."""
    try:
        action()
    except (ValueError, OSError) as error:
        return error

    return None


class HostedFixture:
    """Model only the CLI responses consumed by the release protocol; never spawn processes."""

    def __init__(self, candidate, provenance, manifest):
        self.candidate = candidate
        self.provenance = provenance
        self.manifest = manifest
        self.calls = []
        self.releases = []
        self.assets = []
        self.crypto_failure = False
        self.signer_failure = False
        self.github_signature = True
        self.hidden_digests = False
        self.remote_commit = manifest["source"]["commit"]
        self.verification_pending = 0
        self.verification_failure_commands = []
        self.sleep_calls = []
        self.local_dirty = False
        self.remote_tag_exists = False
        self.api_overrides = {}
        self.during_signer_verification = None

    @property
    def writes(self):
        """Expose every simulated remote write for guard-order assertions."""
        return [call for call in self.calls if call[:2] == ["gh", "release"]
                and call[2] in ("create", "upload", "edit")]

    def existing_release(self, *, draft=True, assets=None):
        """Create matching prior state so tests can change one recovery invariant."""
        self.releases = [{"id": 41, "tag_name": self.manifest["tag"], "name": self.manifest["tag"],
                          "body": (self.candidate / "release-notes.md").read_text(encoding="utf-8"),
                          "draft": draft, "immutable": not draft,
                          "prerelease": "-" in self.manifest["version"],
                          "html_url": f"https://github.com/{release.REPOSITORY}/releases/tag/{self.manifest['tag']}"}]
        expected = release.expected_assets(self.candidate, self.provenance)
        selection = expected if assets is None else assets
        self.assets = [{"name": name, "size": expected[name]["size"], "digest": "sha256:" + expected[name]["sha256"],
                        "state": "uploaded"} for name in selection]

    def run(self, arguments):
        """Return deterministic fake CLI responses and reject unmodeled operations."""
        arguments = list(arguments)
        self.calls.append(arguments)

        if arguments[:3] == ["gh", "attestation", "verify"]:
            if self.crypto_failure:
                raise release.ReleaseError("command-failed", "Fixture signature verification rejected.")

            return "[{\"verified\":true}]"

        if arguments[0] == "git":
            return self.git(arguments)

        if arguments[:2] == ["gh", "api"]:
            return self.api(arguments[-1])

        if arguments[:3] == ["gh", "release", "create"]:
            self.existing_release(assets=[])

            return ""

        if arguments[:3] == ["gh", "release", "upload"]:
            path = Path(arguments[4])
            self.assets.append({"name": path.name, "size": path.stat().st_size,
                                "digest": "sha256:" + sha256(path), "state": "uploaded"})

            return ""

        if arguments[:3] == ["gh", "release", "edit"]:
            self.releases[0]["draft"] = False
            self.releases[0]["immutable"] = True

            return ""

        if arguments[:2] == ["gh", "release"] and arguments[2] in ("verify", "verify-asset"):
            if arguments[2] in self.verification_failure_commands:
                self.verification_failure_commands.remove(arguments[2])
                raise release.ReleaseError("command-failed", "Fixture attestation is pending.")

            if self.verification_pending:
                self.verification_pending -= 1
                raise release.ReleaseError("command-failed", "Fixture attestation is pending.")

            return '{"verified":true}'

        raise AssertionError(f"Unmodeled command: {arguments}")

    def git(self, arguments):
        """Simulate read-only Git source/tag/signature queries."""
        commit = self.manifest["source"]["commit"]
        simple = {("remote", "get-url", "origin"): f"https://github.com/{release.REPOSITORY}.git",
                  ("rev-parse", "HEAD"): commit, ("rev-parse", "HEAD^{tree}"): self.manifest["source"]["tree"],
                  ("status", "--porcelain"): " M changed.py" if self.local_dirty else "",
                  ("branch", "--show-current"): "main", ("config", "--get", "gpg.format"): "ssh",
                  ("config", "--get", "user.signingkey"): "key-from-existing-operator-configuration"}

        if tuple(arguments[1:]) in simple:
            return simple[tuple(arguments[1:])]

        if arguments[1] == "ls-remote":
            reference = arguments[-1]

            if reference == "refs/heads/main":
                return f"{self.remote_commit}\trefs/heads/main\n"

            return f"{'b' * 40}\t{reference}\n" if self.remote_tag_exists else ""

        if arguments[1:3] == ["merge-base", "--is-ancestor"]:
            return ""

        if arguments[1:3] == ["cat-file", "-t"]:
            return "tag"

        if arguments[1:3] == ["cat-file", "tag"]:
            return "Fixture tag\n-----BEGIN SSH SIGNATURE-----\nfixture"

        if arguments[1] == "rev-parse":
            return commit if arguments[-1].endswith("^{commit}") else "b" * 40

        if "verify-tag" in arguments:
            if self.during_signer_verification:
                self.during_signer_verification()

            if self.signer_failure:
                raise release.ReleaseError("command-failed", "Fixture signer is not authorized.")

            return ""

        raise AssertionError(f"Unmodeled Git query: {arguments}")

    def api(self, path):
        """Represent authenticated pagination and mutable service observations."""
        path = path.removeprefix(f"repos/{release.REPOSITORY}").lstrip("/")

        if path in self.api_overrides:
            return json.dumps(self.api_overrides[path])

        if not path:
            value = {"full_name": release.REPOSITORY, "private": False, "archived": False}
        elif path == "branches/main":
            value = {"protected": True}
        elif path.startswith("actions/runs/"):
            value = {"id": 1234, "run_attempt": int(path.rsplit("/", 1)[1]), "path": release.WORKFLOW,
                     "head_sha": self.manifest["source"]["commit"], "head_branch": "main", "event": "workflow_dispatch",
                     "repository": {"full_name": release.REPOSITORY}}
        elif path.startswith("git/ref/tags/"):
            value = {"object": {"type": "tag", "sha": "b" * 40}}
        elif path.startswith("git/tags/"):
            value = {"tag": self.manifest["tag"],
                     "object": {"type": "commit", "sha": self.manifest["source"]["commit"]},
                     "verification": {"verified": self.github_signature,
                                      "reason": "valid" if self.github_signature else "unknown_key"}}
        elif path == "releases?per_page=100":
            value = [[], self.releases]
        elif path == "releases/41/assets?per_page=100":
            entries = copy.deepcopy(self.assets)

            if self.hidden_digests:
                for entry in entries:
                    entry["digest"] = None

            value = [entries[:1], entries[1:]]
        elif path == "releases/latest":
            value = self.releases[0]
        else:
            raise AssertionError(f"Unmodeled API read: {path}")

        return json.dumps(value)

    def sleep(self, seconds):
        """Advance the fixture observation count without real delays."""
        self.sleep_calls.append(seconds)


class PublicationTests(unittest.TestCase):
    """Prove refusal paths, same-candidate recovery, and exact completion inputs offline."""

    def setUp(self):
        """Create actual hashed files plus structurally complete fake signed statements."""
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.directory = Path(self.temporary.name)
        source_props = self.directory / "src/Directory.Build.props"
        source_props.parent.mkdir()
        source_props.write_text("<Project><PropertyGroup><VersionPrefix>1.2.3</VersionPrefix>"
                                "</PropertyGroup></Project>\n", encoding="ascii")
        (self.directory / "CHANGELOG.md").write_text("# Changelog\n\n## 1.2.3-rc.1 (2026-09-19)\n\n"
                                                       "Reviewed release.\n", encoding="ascii")
        self.candidate = self.directory / "candidate"
        self.provenance = self.directory / "provenance"
        self.candidate.mkdir()
        self.provenance.mkdir()
        self.version = "1.2.3-rc.1"
        self.output = self.directory / "result.json"
        self.packages = []

        for package in release.PACKAGES:
            record = {"id": package, "version": self.version, "symbols": [{"pdbName": f"{package}.pdb",
                      "url": f"https://symbols.nuget.org/download/symbols/{package}.pdb/fixture/{package}.pdb",
                      "checksum": "d" * 64, "sha256": "e" * 64}]}

            for extension in ("nupkg", "snupkg"):
                name = f"{package}.{self.version}.{extension}"
                path = self.candidate / name
                path.write_bytes(f"fixture {name}".encode("ascii"))
                record[extension] = name
                record[extension + "Sha256"] = sha256(path)

            self.packages.append(record)
            write_json(self.candidate / f"{package}.spdx.json", {"spdxVersion": "SPDX-2.2", "name": package})

        write_json(self.candidate / "package-manifest.json", {"packages": self.packages})
        (self.candidate / "qualification-evidence.zip").write_bytes(b"sealed fixture evidence")
        (self.candidate / "release-notes.md").write_text("Exact release notes.\n", encoding="ascii")
        self.manifest = {"schemaVersion": 1, "repository": release.REPOSITORY, "version": self.version,
                         "tag": "v" + self.version, "publishable": True, "stages": list(STAGES),
                         "source": {"commit": "a" * 40, "tree": "c" * 40, "fingerprint": "f" * 64},
                         "workflow": {"runId": "1234", "attempt": 1, "ref": "refs/heads/main",
                                      "workflow": release.WORKFLOW}, "files": file_inventory(self.candidate)}
        write_json(self.candidate / "candidate.json", self.manifest)
        self.statement = self.make_statement()
        self.write_bundle("release-provenance.jsonl", self.statement)

        for package in self.packages:
            statement = {"_type": "https://in-toto.io/Statement/v1", "predicateType": "https://spdx.dev/Document/v2.2",
                         "subject": [{"name": package["nupkg"], "digest": {"sha256": package["nupkgSha256"]}}],
                         "predicate": {"spdxVersion": "SPDX-2.2", "name": package["id"]}}
            self.write_bundle(f"sbom-{package['id']}.jsonl", statement)

        self.environment = {"GITHUB_ACTIONS": "true", "GITHUB_REPOSITORY": release.REPOSITORY,
                            "GITHUB_REF": "refs/heads/main", "GITHUB_SHA": self.manifest["source"]["commit"],
                            "GITHUB_WORKFLOW_REF": f"{release.REPOSITORY}/{release.WORKFLOW}@refs/heads/main",
                            "GITHUB_EVENT_NAME": "workflow_dispatch", "GITHUB_RUN_ID": "1234",
                            "GITHUB_RUN_ATTEMPT": "1",
                            "RELEASE_ALLOWED_SIGNERS": "fixture-principal ssh-ed25519 fixture-public-key\n"}
        self.hosted = HostedFixture(self.candidate, self.provenance, self.manifest)
        self.options = {"commands": self.hosted, "environment": self.environment,
                        "attempts": 3, "sleep": self.hosted.sleep}
        self.readback = self.directory / "nuget-readback.json"
        self.public_packages = [{"id": item["id"], "version": self.version, "nupkgSha256": item["nupkgSha256"],
                                 "snupkgSha256": item["snupkgSha256"], "publicNupkgSha256": "c" * 64,
                                 "canonicalSha256": "d" * 64, "signatureVerified": True,
                                 "symbols": [{**symbol, "verified": True} for symbol in item["symbols"]]}
                                for item in self.packages]
        self.write_readback()

    def make_statement(self):
        """Mirror the official GitHub SLSA workflow predicate and invocation identity."""
        names = ["candidate.json", *(item[extension] for item in self.packages for extension in ("nupkg", "snupkg"))]

        return {"_type": "https://in-toto.io/Statement/v1", "predicateType": "https://slsa.dev/provenance/v1",
                "subject": [{"name": name, "digest": {"sha256": sha256(self.candidate / name)}} for name in names],
                "predicate": {"buildDefinition": {"buildType": "https://actions.github.io/buildtypes/workflow/v1",
                    "externalParameters": {"workflow": {"repository": f"https://github.com/{release.REPOSITORY}",
                                            "path": release.WORKFLOW, "ref": "refs/heads/main"}},
                    "resolvedDependencies": [{"uri": f"git+https://github.com/{release.REPOSITORY}@refs/heads/main",
                                              "digest": {"gitCommit": self.manifest["source"]["commit"]}}]},
                    "runDetails": {"metadata": {"invocationId": f"https://github.com/{release.REPOSITORY}"
                                               "/actions/runs/1234/attempts/1"}}}}

    def write_bundle(self, name, statement):
        """Serialize fake signed input; the injected verifier alone models signature acceptance."""
        bundle = {"dsseEnvelope": {"payloadType": "application/vnd.in-toto+json", "signatures": [{"sig": "fixture"}],
                                   "payload": base64.b64encode(json.dumps(statement).encode("ascii")).decode("ascii")}}
        (self.provenance / name).write_text(json.dumps(bundle) + "\n", encoding="ascii")

    def write_readback(self):
        """Create the package module's exact successful public-observation receipt shape."""
        write_json(self.readback, {"schemaVersion": 1, "repository": release.REPOSITORY, "version": self.version,
                                  "success": True, "packages": self.public_packages})

    def stage(self):
        """Run the sole staging action against fixture-only boundaries."""
        return release.stage(self.candidate, self.provenance, self.output, **self.options)

    def publish(self):
        """Run the sole finalization action against fixture-only boundaries."""
        return release.publish_release(self.candidate, self.provenance, self.readback, self.output, **self.options)

    def test_stage_verifies_every_signature_before_first_remote_write(self):
        """All five provenance subjects and both SBOM signatures guard draft creation."""
        # Act
        result = self.stage()

        # Assert
        first_write = self.hosted.calls.index(self.hosted.writes[0])
        verification = [call for call in self.hosted.calls[:first_write] if call[:3] == ["gh", "attestation", "verify"]]
        self.assertEqual(7, len(verification))
        self.assertTrue(all("--deny-self-hosted-runners" in call and "--source-digest" in call
                            for call in verification))
        self.assertEqual("staged", result["state"])
        self.assertEqual(set(release.expected_assets(self.candidate, self.provenance)), set(result["assets"]))
        self.assertFalse(any("--clobber" in call for call in self.hosted.calls))

    def test_changed_candidate_bytes_block_all_writes(self):
        """A valid filename cannot conceal a modified package after sealing."""
        # Arrange
        (self.candidate / self.packages[0]["nupkg"]).write_bytes(b"tampered")

        # Act
        error = record_error(self.stage)

        # Assert
        self.assertRegex(str(error), "candidate_changed")
        self.assertEqual([], self.hosted.writes)

    def test_foreign_repository_candidate_blocks_all_commands(self):
        """A foreign candidate cannot borrow the canonical repository's publication authority."""
        # Arrange
        self.manifest["repository"] = "foreign/repository"
        write_json(self.candidate / "candidate.json", self.manifest)

        # Act
        error = record_error(self.stage)

        # Assert
        self.assertRegex(str(error), "candidate_identity")
        self.assertEqual([], self.hosted.calls)

    def test_foreign_current_run_blocks_all_commands(self):
        """Publication cannot reuse another run's otherwise unchanged candidate."""
        # Arrange
        self.environment["GITHUB_RUN_ID"] = "9000"

        # Act
        error = record_error(self.stage)

        # Assert
        self.assertRegex(str(error), "hosted-identity")
        self.assertEqual([], self.hosted.calls)

    def test_missing_provenance_blocks_all_commands(self):
        """A missing retained bundle requires recovery, never unverified publication."""
        # Arrange
        (self.provenance / "release-provenance.jsonl").unlink()

        # Act
        error = record_error(self.stage)

        # Assert
        self.assertRegex(str(error), "provenance-inventory")
        self.assertEqual([], self.hosted.calls)

    def test_duplicate_attested_subject_blocks_all_commands(self):
        """An apparent complete subject list must also be unique."""
        # Arrange
        self.statement["subject"].append(self.statement["subject"][0])
        self.write_bundle("release-provenance.jsonl", self.statement)

        # Act
        error = record_error(self.stage)

        # Assert
        self.assertRegex(str(error), "subjects-mismatch")
        self.assertEqual([], self.hosted.calls)

    def test_wrong_subject_hash_blocks_all_commands(self):
        """Signed metadata must bind actual candidate bytes."""
        # Arrange
        self.statement["subject"][0]["digest"]["sha256"] = "0" * 64
        self.write_bundle("release-provenance.jsonl", self.statement)

        # Act
        error = record_error(self.stage)

        # Assert
        self.assertRegex(str(error), "subject-digest")
        self.assertEqual([], self.hosted.calls)

    def test_wrong_source_workflow_blocks_all_commands(self):
        """A signed invocation of a different workflow cannot authorize these bytes."""
        # Arrange
        self.statement["predicate"]["buildDefinition"]["externalParameters"]["workflow"]["path"] = "foreign.yml"
        self.write_bundle("release-provenance.jsonl", self.statement)

        # Act
        error = record_error(self.stage)

        # Assert
        self.assertRegex(str(error), "provenance-workflow")
        self.assertEqual([], self.hosted.calls)

    def test_changed_source_digest_blocks_all_commands(self):
        """Subject equality cannot hide provenance produced from different source."""
        # Arrange
        self.statement["predicate"]["buildDefinition"]["resolvedDependencies"][0]["digest"]["gitCommit"] = "0" * 40
        self.write_bundle("release-provenance.jsonl", self.statement)

        # Act
        error = record_error(self.stage)

        # Assert
        self.assertRegex(str(error), "provenance-source")
        self.assertEqual([], self.hosted.calls)

    def test_null_predicate_is_explicitly_rejected(self):
        """Malformed evidence produces a named refusal rather than escaping validation."""
        # Arrange
        self.statement["predicate"] = None
        self.write_bundle("release-provenance.jsonl", self.statement)

        # Act
        error = record_error(self.stage)

        # Assert
        self.assertRegex(str(error), "predicate-shape")
        self.assertEqual([], self.hosted.calls)

    def test_signed_sbom_must_equal_candidate_document(self):
        """A valid signature over an unrelated SPDX document is insufficient."""
        # Arrange
        name = f"sbom-{release.PACKAGES[0]}.jsonl"
        statement = release.statements(self.provenance / name)
        statement["predicate"]["name"] = "Unrelated package"
        self.write_bundle(name, statement)

        # Act
        error = record_error(self.stage)

        # Assert
        self.assertRegex(str(error), "sbom-predicate")
        self.assertEqual([], self.hosted.writes)

    def test_foreign_provenance_run_blocks_all_commands(self):
        """A signature from the right workflow but wrong invocation is insufficient."""
        # Arrange
        self.statement["predicate"]["runDetails"]["metadata"]["invocationId"] = (
            f"https://github.com/{release.REPOSITORY}/actions/runs/9999/attempts/1")
        self.write_bundle("release-provenance.jsonl", self.statement)

        # Act
        error = record_error(self.stage)

        # Assert
        self.assertRegex(str(error), "provenance-run")
        self.assertEqual([], self.hosted.calls)

    def test_future_attestation_attempt_blocks_all_commands(self):
        """A future attempt cannot become evidence for an earlier publication attempt."""
        # Arrange
        self.statement["predicate"]["runDetails"]["metadata"]["invocationId"] = (
            f"https://github.com/{release.REPOSITORY}/actions/runs/1234/attempts/2")
        self.write_bundle("release-provenance.jsonl", self.statement)

        # Act
        error = record_error(self.stage)

        # Assert
        self.assertRegex(str(error), "provenance-run")
        self.assertEqual([], self.hosted.calls)

    def test_same_run_later_attempt_reuses_frozen_candidate(self):
        """Rerunning publication does not require repacking the earlier qualified attempt."""
        # Arrange
        self.environment["GITHUB_RUN_ATTEMPT"] = "3"
        candidate_hash = sha256(self.candidate / "candidate.json")

        # Act
        result = self.stage()

        # Assert
        self.assertEqual(candidate_hash, result["candidateSha256"])
        self.assertEqual(1, result["workflow"]["attempt"])
        self.assertEqual({"runId": "1234", "attempt": "3"}, result["observation"])

    def test_later_attestation_attempt_can_bind_earlier_candidate(self):
        """An attestation retry preserves the earlier qualification attempt's exact files."""
        # Arrange
        self.environment["GITHUB_RUN_ATTEMPT"] = "3"
        self.statement["predicate"]["runDetails"]["metadata"]["invocationId"] = (
            f"https://github.com/{release.REPOSITORY}/actions/runs/1234/attempts/2")
        self.write_bundle("release-provenance.jsonl", self.statement)

        # Act
        result = self.stage()

        # Assert
        self.assertEqual("staged", result["state"])
        self.assertEqual(1, result["workflow"]["attempt"])

    def test_crypto_rejection_blocks_all_writes(self):
        """Structurally correct JSON is not cryptographic evidence."""
        # Arrange
        self.hosted.crypto_failure = True

        # Act
        error = record_error(self.stage)

        # Assert
        self.assertRegex(str(error), "signature verification rejected")
        self.assertEqual([], self.hosted.writes)

    def test_unapproved_ssh_signer_blocks_all_writes(self):
        """The external allowed-signers policy must authorize the actual tag signature."""
        # Arrange
        self.hosted.signer_failure = True

        # Act
        error = record_error(self.stage)

        # Assert
        self.assertRegex(str(error), "signer is not authorized")
        self.assertEqual([], self.hosted.writes)

    def test_missing_signer_policy_blocks_all_writes(self):
        """No default or invented operator key may authorize publication."""
        # Arrange
        del self.environment["RELEASE_ALLOWED_SIGNERS"]

        # Act
        error = record_error(self.stage)

        # Assert
        self.assertRegex(str(error), "signer-policy-missing")
        self.assertEqual([], self.hosted.writes)

    def test_candidate_drift_during_tag_verification_blocks_writes(self):
        """Earlier attestation success cannot authorize files changed during remote source checks."""
        # Arrange
        path = self.candidate / self.packages[0]["nupkg"]
        self.hosted.during_signer_verification = lambda: path.write_bytes(b"changed after attestation")

        # Act
        error = record_error(self.stage)

        # Assert
        self.assertRegex(str(error), "evidence-changed")
        self.assertEqual([], self.hosted.writes)

    def test_provenance_drift_during_tag_verification_blocks_writes(self):
        """The portable bundle itself remains the exact bundle cryptographically verified earlier."""
        # Arrange
        path = self.provenance / "release-provenance.jsonl"
        self.hosted.during_signer_verification = lambda: path.write_bytes(b"changed after verification")

        # Act
        error = record_error(self.stage)

        # Assert
        self.assertRegex(str(error), "evidence-changed")
        self.assertEqual([], self.hosted.writes)

    def test_remote_tag_object_mismatch_blocks_writes(self):
        """Remote tag movement cannot borrow a valid signature from the previously fetched local tag."""
        # Arrange
        self.hosted.api_overrides[f"git/ref/tags/{self.manifest['tag']}"] = {"object": {"type": "tag", "sha": "0" * 40}}

        # Act
        error = record_error(self.stage)

        # Assert
        self.assertRegex(str(error), "remote-tag-mismatch")
        self.assertEqual([], self.hosted.writes)

    def test_github_unverified_tag_blocks_all_writes(self):
        """A locally accepted key does not bypass the required GitHub signature verdict."""
        # Arrange
        self.hosted.github_signature = False

        # Act
        error = record_error(self.stage)

        # Assert
        self.assertRegex(str(error), "remote-tag-signature")
        self.assertEqual([], self.hosted.writes)

    def test_wrong_github_run_metadata_blocks_all_writes(self):
        """Authenticated run metadata must agree with injected workflow identity variables."""
        # Arrange
        self.hosted.api_overrides["actions/runs/1234/attempts/1"] = {"id": 9999}

        # Act
        error = record_error(self.stage)

        # Assert
        self.assertRegex(str(error), "run-mismatch")
        self.assertEqual([], self.hosted.writes)

    def test_existing_draft_notes_conflict_blocks_all_writes(self):
        """Matching tag names do not authorize replacing different release notes."""
        # Arrange
        self.hosted.existing_release()
        self.hosted.releases[0]["body"] = "Different notes"

        # Act
        error = record_error(self.stage)

        # Assert
        self.assertRegex(str(error), "release-metadata")
        self.assertEqual([], self.hosted.writes)

    def test_duplicate_drafts_across_pages_block_all_writes(self):
        """Authenticated pagination cannot silently select one of several matching drafts."""
        # Arrange
        self.hosted.existing_release()
        self.hosted.releases.append(copy.deepcopy(self.hosted.releases[0]))

        # Act
        error = record_error(self.stage)

        # Assert
        self.assertRegex(str(error), "release-duplicate")
        self.assertEqual([], self.hosted.writes)

    def test_unexpected_existing_asset_blocks_all_writes(self):
        """Unapproved release assets cannot accompany a matching candidate."""
        # Arrange
        self.hosted.existing_release()
        self.hosted.assets.append({"name": "foreign.zip", "size": 1, "digest": "sha256:" + "0" * 64})

        # Act
        error = record_error(self.stage)

        # Assert
        self.assertRegex(str(error), "release-assets")
        self.assertEqual([], self.hosted.writes)

    def test_duplicate_asset_name_across_pages_blocks_all_writes(self):
        """A digest map must not silently collapse duplicate release asset names."""
        # Arrange
        self.hosted.existing_release()
        self.hosted.assets.append(copy.deepcopy(self.hosted.assets[0]))

        # Act
        error = record_error(self.stage)

        # Assert
        self.assertRegex(str(error), "release-assets")
        self.assertEqual([], self.hosted.writes)

    def test_receipt_cannot_mutate_sealed_candidate(self):
        """An output path mistake must be caught before draft creation."""
        # Arrange
        self.output = self.candidate / "candidate.json"

        # Act
        error = record_error(self.stage)

        # Assert
        self.assertRegex(str(error), "receipt-location")
        self.assertEqual([], self.hosted.calls)

    def test_changed_existing_asset_hash_is_terminal(self):
        """A public byte conflict is rejected without polling or clobbering."""
        # Arrange
        self.hosted.existing_release()
        self.hosted.assets[0]["digest"] = "sha256:" + "0" * 64

        # Act
        error = record_error(self.stage)

        # Assert
        self.assertRegex(str(error), "asset-digest")
        self.assertEqual([], self.hosted.writes)
        self.assertEqual([], self.hosted.sleep_calls)

    def test_partial_draft_only_uploads_missing_assets(self):
        """Same-candidate retry retains existing matching bytes and resumes absent uploads."""
        # Arrange
        retained = self.packages[0]["nupkg"]
        self.hosted.existing_release(assets=[retained])

        # Act
        result = self.stage()

        # Assert
        uploaded = [Path(call[4]).name for call in self.hosted.writes]
        self.assertNotIn(retained, uploaded)
        self.assertEqual(set(result["assets"]) - {retained}, set(uploaded))
        self.assertTrue(all(call[2] == "upload" for call in self.hosted.writes))

    def test_matching_immutable_release_is_never_mutated(self):
        """A completed same-candidate retry cannot modify immutable public state."""
        # Arrange
        self.hosted.existing_release(draft=False)

        # Act
        result = self.stage()

        # Assert
        self.assertTrue(result["release"]["immutable"])
        self.assertEqual([], self.hosted.writes)

    def test_missing_remote_digest_wait_is_bounded(self):
        """Missing digest metadata remains incomplete even when every asset name is visible."""
        # Arrange
        self.hosted.existing_release()
        self.hosted.hidden_digests = True

        # Act
        error = record_error(self.stage)

        # Assert
        self.assertRegex(str(error), "release-timeout")
        self.assertEqual([10, 10], self.hosted.sleep_calls)
        self.assertFalse(self.output.exists())

    def test_missing_nuget_readback_blocks_publish(self):
        """Staged assets alone cannot authorize publishing the GitHub draft."""
        # Arrange
        self.hosted.existing_release()
        self.readback.unlink()

        # Act
        error = record_error(self.publish)

        # Assert
        self.assertIsNotNone(error)
        self.assertEqual([], self.hosted.writes)

    def test_missing_repository_signature_blocks_publish(self):
        """Matching payloads do not replace the required NuGet signature verification."""
        # Arrange
        self.hosted.existing_release()
        self.public_packages[0]["signatureVerified"] = False
        self.write_readback()

        # Act
        error = record_error(self.publish)

        # Assert
        self.assertRegex(str(error), "nuget-package-readback")
        self.assertEqual([], self.hosted.writes)

    def test_mismatched_public_symbol_blocks_publish(self):
        """Package publication does not count as completed public symbol readback."""
        # Arrange
        self.hosted.existing_release()
        self.public_packages[1]["symbols"][0]["sha256"] = "0" * 64
        self.write_readback()

        # Act
        error = record_error(self.publish)

        # Assert
        self.assertRegex(str(error), "nuget-package-readback")
        self.assertEqual([], self.hosted.writes)

    def test_publish_verifies_immutable_release_and_each_asset(self):
        """A successful write is followed by independent release and per-asset attestations."""
        # Arrange
        self.hosted.existing_release()

        # Act
        result = self.publish()

        # Assert
        checks = [call for call in self.hosted.calls if call[:3] == ["gh", "release", "verify-asset"]]
        self.assertEqual(len(result["assets"]), len(checks))
        self.assertEqual("published", result["state"])
        self.assertTrue(result["release"]["immutable"])
        self.assertEqual(sha256(self.readback), result["nugetReadbackSha256"])
        self.assertEqual(["edit"], [call[2] for call in self.hosted.writes])

    def test_pending_immutable_attestation_can_converge(self):
        """Asynchronous release attestation requires a later successful verification."""
        # Arrange
        self.hosted.existing_release()
        self.hosted.verification_pending = 1

        # Act
        result = self.publish()

        # Assert
        self.assertEqual("published", result["state"])
        self.assertEqual([10], self.hosted.sleep_calls)

    def test_immutable_attestation_timeout_never_reports_success(self):
        """A prior remote publication remains visible when completion verification fails."""
        # Arrange
        self.hosted.existing_release()
        self.hosted.verification_pending = 20

        # Act
        error = record_error(self.publish)

        # Assert
        self.assertRegex(str(error), "command-failed")
        self.assertEqual([10, 10], self.hosted.sleep_calls)
        self.assertFalse(self.output.exists())
        self.assertFalse(self.hosted.releases[0]["draft"])

    def test_immutable_verification_retries_are_shared_across_assets(self):
        """Each asset cannot reset the retry budget and multiply the publication duration."""
        # Arrange
        self.hosted.existing_release()
        self.hosted.verification_failure_commands = ["verify", "verify-asset"]
        self.options["attempts"] = 2

        # Act
        error = record_error(self.publish)

        # Assert
        self.assertRegex(str(error), "command-failed")
        self.assertEqual([10], self.hosted.sleep_calls)
        self.assertFalse(self.output.exists())

    def test_completion_rechecks_public_assets_without_writes(self):
        """A final receipt binds candidate, source, provenance, NuGet, and immutable release evidence."""
        # Arrange
        self.hosted.existing_release(draft=False)

        # Act
        result = release.complete(self.candidate, self.provenance, self.readback, self.output, **self.options)

        # Assert
        self.assertEqual("complete", result["state"])
        self.assertEqual(self.manifest["source"], result["source"])
        self.assertTrue(result["nuget"]["success"])
        self.assertEqual([], self.hosted.writes)

    def test_pre_tag_only_reads_git_and_does_not_fetch(self):
        """Operator inspection cannot create local refs, tags, commits, or remote state."""
        # Act
        result = release.pre_tag(self.version, self.candidate, commands=self.hosted,
                                 source_repo=self.directory)

        # Assert
        self.assertEqual("pre-tag-checked", result["state"])
        self.assertTrue(all(call[0] == "git" for call in self.hosted.calls))
        self.assertFalse(any(call[1] in ("fetch", "tag", "commit", "push", "add") for call in self.hosted.calls))

    def test_pre_tag_dirty_source_is_rejected(self):
        """A local inspection cannot label uncommitted source release-ready."""
        # Arrange
        self.hosted.local_dirty = True

        # Act
        error = record_error(lambda: release.pre_tag(self.version, commands=self.hosted,
                                                     source_repo=self.directory))

        # Assert
        self.assertRegex(str(error), "source-dirty")
        self.assertEqual([], self.hosted.writes)

    def test_preflight_checks_unused_version_without_building(self):
        """The preflight inspects public availability before qualification creates packages."""
        # Arrange
        availability = {"packages": [{"id": package, "state": "absent"} for package in release.PACKAGES]}

        # Act
        with patch("eng.release.nuget.check_unused_version", return_value=availability) as check:
            result = release.preflight(self.version, commands=self.hosted, environment=self.environment,
                                       source_repo=self.directory)

        # Assert
        check.assert_called_once_with(self.version)
        self.assertEqual(availability, result["nuget"])
        self.assertEqual([], self.hosted.writes)

    def test_preflight_rejects_source_version_before_external_reads(self):
        """A mismatched source release line fails before GitHub and NuGet availability queries."""
        # Arrange
        path = self.directory / "src/Directory.Build.props"
        path.write_text(path.read_text(encoding="ascii").replace("1.2.3", "1.3.0"), encoding="ascii")

        # Act
        error = record_error(lambda: release.preflight(self.version, commands=self.hosted,
                                                       environment=self.environment, source_repo=self.directory))

        # Assert
        self.assertRegex(str(error), "source-version")
        self.assertEqual([], self.hosted.calls)


if __name__ == "__main__":
    unittest.main()

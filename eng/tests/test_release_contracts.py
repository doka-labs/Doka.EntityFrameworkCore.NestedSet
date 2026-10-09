"""Offline candidate, documented verifier, and workflow authorization contracts."""

import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import textwrap
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

from eng.release import common
from eng.tests.test_release_qualification import (
    COMMIT,
    VERSION,
    QualificationFixture,
    capture,
    text_file,
)

ROOT = Path(__file__).resolve().parents[2]


class CandidateManifestTests(QualificationFixture):
    """A sealed manifest is an exact, typed identity rather than a collection of plausible fields."""

    def setUp(self):
        """Assemble a coherent fixture through the real qualification code."""
        super().setUp()
        self.candidate = self.complete_fixture()
        self.path = self.candidate / "candidate.json"

    def mutate(self, change):
        """Change one manifest property while retaining every unrelated fixture byte."""
        value = common.read_json(self.path)
        change(value)
        common.write_json(self.path, value)

    def test_complete_candidate_is_accepted(self):
        """The positive fixture exercises the same full inventory as negative cases."""
        # Arrange
        expected = set(self.candidate.iterdir())

        # Act
        manifest = common.load_candidate(self.candidate)

        # Assert
        self.assertEqual(10, len(expected))
        self.assertEqual(COMMIT, manifest["source"]["commit"])
        self.assertTrue(manifest["publishable"])

    def test_nonobject_manifest_is_rejected(self):
        """An array cannot be mistaken for a versioned candidate document."""
        # Arrange
        common.write_json(self.path, [])

        # Act
        error = capture(lambda: common.load_candidate(self.candidate))

        # Assert
        self.assertIsInstance(error, common.ReleaseError)
        self.assertEqual("candidate_schema", error.code)

    def test_boolean_schema_version_is_rejected(self):
        """JSON true is not the integer schema version even though Python compares them equally."""
        # Arrange
        self.mutate(lambda value: value.update(schemaVersion=True))

        # Act
        error = capture(lambda: common.load_candidate(self.candidate))

        # Assert
        self.assertIsInstance(error, common.ReleaseError)

    def test_duplicate_json_key_is_rejected(self):
        """Ambiguous duplicated source identity must fail before field selection."""
        # Arrange
        original = self.path.read_text(encoding="ascii")
        self.path.write_text(original.replace('"schemaVersion": 1', '"schemaVersion": 1, "schemaVersion": 1'), encoding="ascii")

        # Act
        error = capture(lambda: common.load_candidate(self.candidate))

        # Assert
        self.assertIsInstance(error, common.ReleaseError)
        self.assertEqual("duplicate_json_key", error.code)

    def test_missing_file_is_rejected(self):
        """A rewritten manifest cannot turn a missing required symbol archive into a valid candidate."""
        # Arrange
        name = f"Doka.NestedSet.{VERSION}.snupkg"
        (self.candidate / name).unlink()
        self.mutate(lambda value: value["files"].pop(name))

        # Act
        error = capture(lambda: common.load_candidate(self.candidate))

        # Assert
        self.assertIsInstance(error, common.ReleaseError)
        self.assertEqual("candidate_files", error.code)

    def test_unexpected_file_is_rejected_even_when_hashed(self):
        """Hashing an extra executable does not make it part of the allowed candidate inventory."""
        # Arrange
        extra = self.candidate / "unexpected.py"
        text_file(extra, "raise SystemExit('unexpected asset')")
        self.mutate(lambda value: value["files"].update({extra.name: {"size": extra.stat().st_size,
                                                                   "sha256": common.sha256(extra)}}))

        # Act
        error = capture(lambda: common.load_candidate(self.candidate))

        # Assert
        self.assertIsInstance(error, common.ReleaseError)
        self.assertEqual("candidate_files", error.code)

    def test_symlink_asset_is_rejected(self):
        """An inventory cannot redirect evidence reads outside its fixed candidate directory."""
        # Arrange
        original = self.candidate / "release-notes.md"
        outside = self.root / "outside.txt"
        original.rename(outside)
        original.symlink_to(outside)

        # Act
        error = capture(lambda: common.load_candidate(self.candidate))

        # Assert
        self.assertIsInstance(error, common.ReleaseError)
        self.assertEqual("linked_evidence", error.code)

    def test_changed_asset_is_rejected(self):
        """Changing primary package bytes invalidates the sealed candidate."""
        # Arrange
        text_file(self.candidate / f"Doka.NestedSet.{VERSION}.nupkg", "changed package")

        # Act
        error = capture(lambda: common.load_candidate(self.candidate))

        # Assert
        self.assertIsInstance(error, common.ReleaseError)
        self.assertEqual("candidate_changed", error.code)

    def test_stage_reordering_is_rejected(self):
        """Gate ordering is part of qualification rather than an unordered success list."""
        # Arrange
        self.mutate(lambda value: value["stages"].reverse())

        # Act
        error = capture(lambda: common.load_candidate(self.candidate))

        # Assert
        self.assertIsInstance(error, common.ReleaseError)
        self.assertEqual("incomplete_qualification", error.code)

    def test_malformed_source_fingerprint_is_rejected(self):
        """Null source metadata fails with the release diagnostic rather than a parser type error."""
        # Arrange
        self.mutate(lambda value: value["source"].update(fingerprint=None))

        # Act
        error = capture(lambda: common.load_candidate(self.candidate))

        # Assert
        self.assertIsInstance(error, common.ReleaseError)
        self.assertEqual("source_identity", error.code)

    def test_boolean_attempt_is_rejected(self):
        """A true flag is not a hosted producer attempt."""
        # Arrange
        self.mutate(lambda value: value["workflow"].update(attempt=True))

        # Act
        error = capture(lambda: common.load_candidate(self.candidate))

        # Assert
        self.assertIsInstance(error, common.ReleaseError)
        self.assertEqual("workflow_identity", error.code)

    def test_foreign_repository_is_rejected(self):
        """A different repository cannot borrow this release manifest format."""
        # Arrange
        self.mutate(lambda value: value.update(repository="other/NestedSet"))

        # Act
        error = capture(lambda: common.load_candidate(self.candidate))

        # Assert
        self.assertIsInstance(error, common.ReleaseError)
        self.assertEqual("candidate_identity", error.code)

    def test_foreign_signing_workflow_is_rejected(self):
        """Development CI is not the protected publication producer."""
        # Arrange
        self.mutate(lambda value: value["workflow"].update(workflow=".github/workflows/ci.yml"))

        # Act
        error = capture(lambda: common.load_candidate(self.candidate))

        # Assert
        self.assertIsInstance(error, common.ReleaseError)
        self.assertEqual("workflow_identity", error.code)


class VersionPolicyTests(unittest.TestCase):
    """Canonical identities avoid NuGet normalization aliases and ambiguous tags."""

    def test_stable_and_numbered_rc_versions_are_accepted(self):
        """The documented stable and RC forms preserve their exact input identity."""
        # Arrange
        versions = ("0.1.0", "10.20.30", "0.1.0-rc.1", "1.0.0-rc.42")

        # Act
        observed = tuple(common.release_version(value) for value in versions)

        # Assert
        self.assertEqual(versions, observed)

    def test_noncanonical_versions_are_rejected(self):
        """Prefixes, aliases, metadata, unnumbered RCs, and development versions cannot publish."""
        # Arrange
        versions = ("v0.1.0", "01.0.0", "1.00.0", "1.0.00", "1.0.0-rc.01", "1.0.0-rc.0", "1.0.0-rc",
                    "1.0.0-RC.1", "1.0.0+build.2", "1.0.0-dev", "1.0.0 ", "1.0", None, True)

        # Act
        errors = [capture(lambda value=value: common.release_version(value)) for value in versions]

        # Assert
        self.assertTrue(all(isinstance(error, common.ReleaseError) for error in errors), repr(errors))
        self.assertTrue(all(error.code == "invalid_version" for error in errors))

    def test_development_version_requires_explicit_local_mode(self):
        """The development default is accepted only by the non-publication parsing mode."""
        # Arrange
        version = "0.1.0-dev"

        # Act
        result = common.release_version(version, allow_development=True)

        # Assert
        self.assertEqual(version, result)


class DocumentedConsumerTests(QualificationFixture):
    """Execute the published inventory example offline; no cryptographic success is inferred from fixtures."""

    def setUp(self):
        """Read the actual documentation snippet and prepare its thirteen local release assets."""
        super().setUp()
        candidate = self.complete_fixture()
        self.downloads = self.root / "downloads"
        shutil.copytree(candidate, self.downloads)
        for name in ("release-provenance.jsonl", *(f"sbom-{package}.jsonl" for package in common.PACKAGES)):
            text_file(self.downloads / name, "offline fixture, not a cryptographic attestation")

        self.target = self.root / "consumer"
        self.target.mkdir()
        document = (ROOT / "docs/security/release-verification.md").read_text(encoding="ascii")
        snippets = re.findall(r"<<'PY'\n(.*?)\nPY", document, re.DOTALL)
        self.assertEqual(1, len(snippets), "Review the test when the documented consumer procedure changes")
        self.script = snippets[0]

    def execute_example(self, expected=COMMIT):
        """Run only the documentation's local Python inventory/hash verification."""
        return subprocess.run([sys.executable, "-", str(self.downloads), str(self.target), VERSION, expected],
                              input=self.script, text=True, cwd=ROOT, capture_output=True, timeout=10, check=False)

    def test_documented_example_accepts_valid_inventory(self):
        """The copy-and-verify example must be executable as written."""
        # Arrange
        original = common.file_inventory(self.downloads)

        # Act
        result = self.execute_example()

        # Assert
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("Candidate inventory, hashes, version, and source match", result.stdout)
        self.assertEqual(original, common.file_inventory(self.downloads))

    def test_documented_example_rejects_tampered_bytes(self):
        """A downloaded package changed after signing cannot pass its candidate hash check."""
        # Arrange
        text_file(self.downloads / f"Doka.NestedSet.{VERSION}.nupkg", "tampered")

        # Act
        result = self.execute_example()

        # Assert
        self.assertNotEqual(0, result.returncode)
        self.assertIn("candidate_changed", result.stderr)

    def test_documented_example_rejects_unexpected_asset(self):
        """The consumer must reject an extra file before copying candidate content."""
        # Arrange
        text_file(self.downloads / "unexpected.txt", "extra")

        # Act
        result = self.execute_example()

        # Assert
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Unexpected or missing release asset", result.stderr)
        self.assertEqual([], list(self.target.iterdir()))

    def test_documented_example_rejects_expected_source_shift(self):
        """An independently expected source SHA cannot be replaced by the manifest's claimed source."""
        # Arrange
        foreign_commit = "d" * 40

        # Act
        result = self.execute_example(expected=foreign_commit)

        # Assert
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Candidate version/source mismatch", result.stderr)


def workflow_jobs(path):
    """Read job blocks from the repository's fixed YAML indentation without a new parser dependency."""
    text = path.read_text(encoding="ascii")
    body = text.split("\njobs:\n", 1)[1]
    matches = list(re.finditer(r"^  ([A-Za-z_][A-Za-z0-9_-]*):\s*$", body, re.MULTILINE))

    return {match.group(1): body[match.end():matches[index + 1].start() if index + 1 < len(matches) else len(body)]
            for index, match in enumerate(matches)}


class DependencyLicenseExceptionTests(unittest.TestCase):
    """Exercise the actual inline version guard while preserving the standard license and vulnerability gates."""

    @staticmethod
    def dependency(name, version, change_type="added"):
        """Create a public, synthetic dependency comparison entry."""
        return {"name": name, "version": version, "change_type": change_type,
                "ecosystem": "nuget", "package_url": f"pkg:nuget/{name}@{version}"}

    @staticmethod
    def run_guard(payload):
        """Run the exact workflow command with synthetic action output and no GitHub access."""
        workflow = (ROOT / ".github/workflows/dependency-review.yml").read_text(encoding="ascii")
        step = workflow.split("- name: Require exact license-exception versions", 1)[1]
        command = textwrap.dedent(step.split("        run: |\n", 1)[1])

        return subprocess.run(["bash", "-eo", "pipefail", "-c", command],
                              env={**os.environ, "DEPENDENCY_CHANGES": json.dumps(payload)},
                              capture_output=True, text=True, check=False)

    def test_accepts_reviewed_versions_across_manifests(self):
        """The two approved packages retain narrow exceptions even when several manifests use them."""
        # Arrange
        entries = [self.dependency("Microsoft.Data.SqlClient.SNI.runtime", "6.0.2"),
                   self.dependency("Microsoft.Identity.Client.NativeInterop", "0.20.6")]

        # Act
        result = self.run_guard(entries + entries)

        # Assert
        self.assertEqual(0, result.returncode, result.stderr)

    def test_unrelated_packages_need_no_exception(self):
        """Ordinary dependency changes are still owned by the official license and vulnerability action."""
        # Arrange
        entries = [self.dependency("FsCheck.Xunit.v3", "3.4.0")]

        # Act
        result = self.run_guard(entries)

        # Assert
        self.assertEqual(0, result.returncode, result.stderr)

    def test_empty_delta_is_accepted(self):
        """An empty comparison is a valid action output."""
        # Arrange
        entries = []

        # Act
        result = self.run_guard(entries)

        # Assert
        self.assertEqual(0, result.returncode, result.stderr)

    def test_rejects_sni_version_change(self):
        """An exception cannot silently approve a newer SNI license file."""
        # Arrange
        entries = [self.dependency("Microsoft.Data.SqlClient.SNI.runtime", "6.0.3")]

        # Act
        result = self.run_guard(entries)

        # Assert
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Review license files", result.stderr)

    def test_rejects_nativeinterop_version_change_after_approved_entries(self):
        """All changes are checked, including a second version after an approved occurrence."""
        # Arrange
        entries = [self.dependency("Microsoft.Identity.Client.NativeInterop", "0.20.6"),
                   self.dependency("Microsoft.Identity.Client.NativeInterop", "0.20.7")]

        # Act
        result = self.run_guard(entries)

        # Assert
        self.assertNotEqual(0, result.returncode)

    def test_removing_unapproved_version_is_accepted(self):
        """Removing a dependency does not grant a new license exception."""
        # Arrange
        entries = [self.dependency("Microsoft.Identity.Client.NativeInterop", "99.0.0", "removed")]

        # Act
        result = self.run_guard(entries)

        # Assert
        self.assertEqual(0, result.returncode, result.stderr)

    def test_rejects_inconsistent_package_url(self):
        """Matching display metadata cannot hide a different exempted PURL version."""
        # Arrange
        entry = self.dependency("Microsoft.Data.SqlClient.SNI.runtime", "6.0.2")
        entry["package_url"] = "pkg:nuget/Microsoft.Data.SqlClient.SNI.runtime@6.0.3"

        # Act
        result = self.run_guard([entry])

        # Assert
        self.assertNotEqual(0, result.returncode)

    def test_rejects_inconsistent_package_name(self):
        """The action-exempted PURL must agree with the reported package identity."""
        # Arrange
        entry = self.dependency("Microsoft.Data.SqlClient.SNI.runtime", "6.0.2")
        entry["name"] = "Different.Package"

        # Act
        result = self.run_guard([entry])

        # Assert
        self.assertNotEqual(0, result.returncode)

    def test_rejects_missing_version(self):
        """An incomplete excepted package cannot establish the reviewed license version."""
        # Arrange
        entry = self.dependency("Microsoft.Identity.Client.NativeInterop", "0.20.6")
        entry.pop("version")

        # Act
        result = self.run_guard([entry])

        # Assert
        self.assertNotEqual(0, result.returncode)

    def test_rejects_nonarray_output(self):
        """Malformed action output cannot turn the all-entry guard into an empty success."""
        # Arrange
        payload = {"changes": []}

        # Act
        result = self.run_guard(payload)

        # Assert
        self.assertNotEqual(0, result.returncode)

    def test_rejects_encoded_purl_with_foreign_display_identity(self):
        """Percent encoding cannot hide a package exempted by the action's decoded identity."""
        # Arrange
        entry = self.dependency("Different.Package", "99.0.0")
        entry["package_url"] = "pkg:nuget/%4Dicrosoft.Identity.Client.NativeInterop@99.0.0"

        # Act
        result = self.run_guard([entry])

        # Assert
        self.assertNotEqual(0, result.returncode)

    def test_accepts_encoded_identity_for_the_reviewed_version(self):
        """Equivalent package encoding retains the same reviewed license identity."""
        # Arrange
        entry = self.dependency("Microsoft.Identity.Client.NativeInterop", "0.20.6")
        entry["package_url"] = "pkg:nuget/%4Dicrosoft.Identity.Client.NativeInterop@0.20.6"

        # Act
        result = self.run_guard([entry])

        # Assert
        self.assertEqual(0, result.returncode, result.stderr)

    def test_unrelated_containing_name_is_not_an_exception(self):
        """A substring collision must not extend the two exceptions to a different NuGet package."""
        # Arrange
        entries = [self.dependency("Contoso.Microsoft.Identity.Client.NativeInterop.Adapter", "1.0.0")]

        # Act
        result = self.run_guard(entries)

        # Assert
        self.assertEqual(0, result.returncode, result.stderr)

    def test_rejects_namespace_only_fallback_with_foreign_metadata(self):
        """The permissive action namespace fallback cannot bypass exact package validation."""
        # Arrange
        entry = self.dependency("Different.Package", "99.0.0")
        entry["package_url"] = "pkg:nuget/Microsoft.Data.SqlClient.SNI.runtime/"

        # Act
        result = self.run_guard([entry])

        # Assert
        self.assertNotEqual(0, result.returncode)

    def test_workflow_preserves_general_gates_and_uses_complete_action_output(self):
        """Exceptions are package-specific, version-checked, and do not suppress unrelated license findings."""
        # Arrange
        workflow = (ROOT / ".github/workflows/dependency-review.yml").read_text(encoding="ascii")

        # Act
        exceptions = workflow.split("allow-dependencies-licenses: >-", 1)[1].split("comment-summary-in-pr:", 1)[0]

        # Assert
        self.assertEqual(["pkg:nuget/Microsoft.Data.SqlClient.SNI.runtime@6.0.2",
                          "pkg:nuget/Microsoft.Identity.Client.NativeInterop@0.20.6"],
                         [line.strip().rstrip(",") for line in exceptions.splitlines() if line.strip()])
        self.assertIn("${{ steps.review.outputs.dependency-changes }}", workflow)
        self.assertIn("fail-on-severity: high", workflow)
        self.assertIn("allow-licenses:", workflow)
        self.assertNotIn("LicenseRef-scancode-unknown", workflow)
        self.assertNotIn("license-check: false", workflow)
        self.assertNotIn("warn-only: true", workflow)


class WorkflowBoundaryTests(unittest.TestCase):
    """Check qualification and authorization sequencing without running GitHub actions."""

    def setUp(self):
        """Read the actual workflow sources under review."""
        self.ci_path = ROOT / ".github/workflows/ci.yml"
        self.release_path = ROOT / common.RELEASE_WORKFLOW
        self.ci = workflow_jobs(self.ci_path)
        self.release = workflow_jobs(self.release_path)

    def test_ci_checks_csharp_and_rc_qualifies_complete_candidate(self):
        """PR checks stay direct while release-specific services belong to the RC."""
        # Arrange
        ci = "\n".join(self.ci.values())
        release = self.release["qualify"]
        triggers = self.ci_path.read_text(encoding="ascii").split("\non:\n", 1)[1].split("\npermissions:", 1)[0]

        # Act
        ci_commands = {name for name in ("dotnet restore", "dotnet build", "dotnet test", "dotnet pack") if name in ci}
        release_runs = release.count("bash eng/release-candidate.sh")

        # Assert
        self.assertEqual({"dotnet restore", "dotnet build", "dotnet test", "dotnet pack"}, ci_commands)
        self.assertEqual(1, release_runs)
        self.assertNotIn("eng/release-candidate.sh", ci)
        self.assertNotIn("generate-sbom.sh", ci)
        self.assertIn("unittest discover -s eng/tests", ci)
        self.assertIn("unittest discover -s eng/ci-tests", ci)
        self.assertNotIn("--stage", release)
        self.assertNotIn("--resume", release)
        self.assertNotIn("attest", self.ci)
        self.assertIn('$CANDIDATE_VERSION', release)
        self.assertIn("  pull_request:", triggers)
        self.assertIn("    branches: [main]", triggers)
        self.assertNotIn("  push:", triggers)
        self.assertNotIn("  schedule:", triggers)

    def test_ci_producers_run_independently(self):
        """Unrelated checks and all matrix cells remain eligible to run concurrently."""
        # Arrange
        producers = ("source-quality", "engineering-tests", "build", "tests", "samples")

        # Act
        dependencies = {name: re.findall(r"^    needs:.*$", self.ci[name], re.MULTILINE) for name in producers}

        # Assert
        self.assertEqual({name: [] for name in producers}, dependencies)
        self.assertNotIn("max-parallel:", self.ci["tests"])
        self.assertNotIn("max-parallel:", self.ci["samples"])
        self.assertIn("fail-fast: false", self.ci["tests"])
        self.assertIn("fail-fast: false", self.ci["samples"])
        self.assertNotIn("-m:1", self.ci["tests"])

    def test_ci_inspects_packed_archives_before_upload(self):
        """PR qualification checks metadata and PE/PDB identity without a second pack or build."""
        # Arrange
        build = self.ci["build"]
        boundaries = ("dotnet pack", "python3 -m eng.release.packages", "name: Retain NuGet packages")

        # Act
        positions = [build.find(boundary) for boundary in boundaries]

        # Assert
        self.assertTrue(all(position >= 0 for position in positions))
        self.assertEqual(sorted(positions), positions)
        self.assertEqual(1, build.count("dotnet pack"))
        self.assertEqual(1, build.count("dotnet build"))
        self.assertIn("-getProperty:PackageVersion", build)
        self.assertIn('--version "$package_version"', build)
        self.assertIn('--source-commit "$(git rev-parse HEAD)"', build)
        self.assertIn("--package-dir artifacts/ci-packages", build)
        self.assertIn("--output artifacts/ci-package-manifest.json", build)
        self.assertIn("            artifacts/ci-package-manifest.json", build)

    def test_ci_matrix_covers_each_executable_test_project_once(self):
        """Adding a suite cannot silently leave it outside pull-request qualification."""
        # Arrange
        projects = list((ROOT / "tests").rglob("*.csproj"))
        expected = {path.relative_to(ROOT).as_posix() for path in projects
                    if ET.parse(path).findtext(".//IsTestProject") != "false"}

        # Act
        selected = re.findall(r"^            project: (\S+)$", self.ci["tests"], re.MULTILINE)

        # Assert
        self.assertEqual(expected, set(selected))
        self.assertEqual(len(selected), len(set(selected)))
        self.assertIn('dotnet restore "${{ matrix.project }}" --locked-mode', self.ci["tests"])
        self.assertIn('dotnet build "${{ matrix.project }}"', self.ci["tests"])
        self.assertIn('dotnet test "${{ matrix.project }}"', self.ci["tests"])
        self.assertNotIn("actions/download-artifact@", self.ci["tests"])

    def test_ci_runs_every_sample(self):
        """Each independent SQLite sample is runnable in the sample matrix."""
        # Arrange
        expected = {path.parent.name for path in (ROOT / "samples").rglob("*.csproj")}

        # Act
        selected = re.search(r"^        sample: \[(.+)\]$", self.ci["samples"], re.MULTILINE)
        names = selected.group(1).split(", ") if selected else []

        # Assert
        self.assertEqual(expected, set(names))
        self.assertEqual(len(names), len(set(names)))
        self.assertIn("--provider sqlite --reset", self.ci["samples"])

    def test_ci_coverage_waits_for_complete_test_evidence(self):
        """The existing two-module verifier reads isolated reports from the current run attempt."""
        # Arrange
        tests = self.ci["tests"]
        coverage = self.ci["coverage"]

        # Act
        dependencies = re.findall(r"^    needs: (.+)$", coverage, re.MULTILINE)

        # Assert
        self.assertEqual(["tests"], dependencies)
        self.assertIn("ci-tests-${{ matrix.id }}-${{ github.run_id }}-${{ github.run_attempt }}", tests)
        self.assertIn("pattern: ci-tests-*-${{ github.run_id }}-${{ github.run_attempt }}", coverage)
        self.assertNotIn("merge-multiple: true", coverage)
        self.assertIn("python3 eng/verify-coverage.py artifacts/ci-tests", coverage)
        self.assertIn("-type f -name '*.cobertura.xml' -print -quit", tests)
        self.assertIn("if-no-files-found: error", tests)
        self.assertNotIn("continue-on-error: true", tests)

    def test_rc_qualifies_without_prior_ci_artifacts(self):
        """A release candidate rebuilds the selected source even if prior CI artifacts expired."""
        # Arrange
        preflight = self.release["preflight"]
        qualify = self.release["qualify"]

        # Act
        previous_artifact_reads = [body for body in (preflight, qualify) if "actions/download-artifact@" in body]

        # Assert
        self.assertEqual([], previous_artifact_reads)
        self.assertIn("bash eng/release-candidate.sh", qualify)

    def test_publication_is_manual_and_non_canceling(self):
        """Tag pushes cannot rebuild an independent candidate or cancel a partially published run."""
        # Arrange
        text = self.release_path.read_text(encoding="ascii")

        # Act
        triggers = text.split("\non:\n", 1)[1].split("\npermissions:", 1)[0]

        # Assert
        self.assertIn("workflow_dispatch:", triggers)
        self.assertNotRegex(triggers, re.compile(r"^  (push|pull_request|workflow_run):", re.MULTILINE))
        self.assertIn("group: nestedset-release-${{ inputs.version }}", text)
        self.assertIn("cancel-in-progress: false", text)

    def test_only_publish_requests_credentials_and_environment(self):
        """Unprotected qualification jobs must not request NuGet publication authority."""
        # Arrange
        publish = self.release["publish"]

        # Act
        credential_jobs = [name for name, body in self.release.items() if "uses: NuGet/login@" in body]
        environment_jobs = [name for name, body in self.release.items() if re.search(r"^    environment:", body, re.MULTILINE)]

        # Assert
        self.assertEqual(["publish"], credential_jobs)
        self.assertEqual(["publish"], environment_jobs)
        self.assertIn("environment: nuget", publish)
        self.assertIn("needs: [qualify, attest]", publish)
        self.assertIn("uses: NuGet/login@8d196754b4036150537f80ac539e15c2f1028841", publish)

    def test_api_token_is_limited_to_jobs_that_use_github(self):
        """Qualification does not inherit the API token used by source and publication guards."""
        # Arrange
        header = self.release_path.read_text(encoding="ascii").split("\njobs:\n", 1)[0]

        # Act
        token_jobs = {name for name, body in self.release.items() if "GH_TOKEN:" in body}

        # Assert
        self.assertNotIn("GH_TOKEN:", header)
        self.assertEqual({"preflight", "attest", "publish"}, token_jobs)

    def test_authorization_and_draft_precede_login(self):
        """Credentials are requested only after source, public-byte, and draft guards."""
        # Arrange
        publish = self.release["publish"]
        boundaries = ("eng.release.publication authorize", "eng.release.nuget availability",
                      "eng.release.publication stage", "uses: NuGet/login@", "dotnet nuget push",
                      "eng.release.nuget readback", "eng.release.publication publish-release", "eng.release.publication complete")

        # Act
        positions = [publish.index(boundary) for boundary in boundaries]

        # Assert
        self.assertEqual(sorted(positions), positions)
        self.assertNotIn("RELEASE_ALLOWED_SIGNERS", publish)

    def test_publication_reuses_immutable_artifact_ids_without_build(self):
        """The protected job receives exact earlier artifacts instead of repacking source."""
        # Arrange
        publish = self.release["publish"]

        # Act
        download_ids = re.findall(r"artifact-ids:\s*(.+)", publish)

        # Assert
        self.assertEqual(["${{ needs.qualify.outputs.artifact-id }}", "${{ needs.attest.outputs.artifact-id }}"], download_ids)
        self.assertNotRegex(publish, r"\bdotnet (?:build|restore|pack)\b")

    def test_primary_packages_precede_all_symbols(self):
        """A symbol upload failure cannot block the dependent EF primary package."""
        # Arrange
        publish = self.release["publish"]
        expected = [f"{package}.$CANDIDATE_VERSION.{extension}"
                    for extension in ("nupkg", "snupkg") for package in common.PACKAGES]

        # Act
        uploads = re.findall(r'dotnet nuget push "artifacts/packages/([^\"]+)"', publish)

        # Assert
        self.assertEqual(expected, uploads)
        self.assertEqual(4, publish.count("--skip-duplicate"))
        self.assertEqual(2, publish.count("--no-symbols"))

    def test_publication_budget_covers_delayed_nuget_indexing(self):
        """A full public readback window must fit inside the protected job with publication headroom."""
        # Arrange
        publish = self.release["publish"]

        # Act
        job_minutes = int(re.search(r"timeout-minutes: (\d+)", publish).group(1))
        readback_seconds = int(re.search(r"--timeout-seconds (\d+)", publish).group(1))

        # Assert
        self.assertEqual(7200, readback_seconds)
        self.assertEqual(180, job_minutes)
        self.assertGreater(job_minutes * 60 - readback_seconds, 2400)

    def test_attestation_covers_manifest_symbols_and_both_sboms(self):
        """Primary archives alone are not the complete provenance boundary."""
        # Arrange
        attest = self.release["attest"]

        # Act
        subjects = re.findall(r"artifacts/candidate/(?:candidate\.json|\*\.nupkg|\*\.snupkg)", attest)

        # Assert
        self.assertEqual({"artifacts/candidate/candidate.json", "artifacts/candidate/*.nupkg", "artifacts/candidate/*.snupkg"}, set(subjects))
        for package in common.PACKAGES:
            self.assertIn(f"sbom-path: artifacts/candidate/{package}.spdx.json", attest)
        self.assertIn("eng.release.publication attest-verify", attest)

    def test_required_check_reports_failures_and_cancellation(self):
        """The stable check cannot disappear when its qualification dependency fails."""
        # Arrange
        matches = [body for body in self.ci.values() if "name: Repository qualification" in body]

        # Act
        required = matches[0] if len(matches) == 1 else ""
        results = set(re.findall(r"\$\{\{ needs\.([\w-]+)\.result \}\}", required))

        # Assert
        self.assertEqual(1, len(matches))
        self.assertIn("needs: [source-quality, engineering-tests, build, tests, samples, coverage]", required)
        self.assertIn("if: always()", required)
        self.assertEqual(set(self.ci) - {"repository-qualification"}, results)

    def run_required_check(self, results):
        """Execute the actual aggregate shell step without a GitHub runner or credentials."""
        script = textwrap.dedent(self.ci["repository-qualification"].split("        run: |\n", 1)[1])

        with tempfile.TemporaryDirectory() as directory:
            environment = os.environ.copy()
            environment["RESULTS"] = results
            environment["GITHUB_STEP_SUMMARY"] = str(Path(directory) / "summary.md")

            return subprocess.run(["bash", "-e", "-o", "pipefail", "-c", script], env=environment,
                                  capture_output=True, text=True, check=False, timeout=10)

    def test_required_check_accepts_all_successful_jobs(self):
        """The stable status check passes when all producers and coverage succeeded."""
        # Arrange
        gates = set(self.ci) - {"repository-qualification"}
        results = " ".join(f"{gate}=success" for gate in sorted(gates))

        # Act
        result = self.run_required_check(results)

        # Assert
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertNotIn("::error", result.stdout)

    def test_required_check_rejects_each_failed_skipped_or_canceled_job(self):
        """One incomplete gate must fail qualification even when all others succeeded."""
        # Arrange
        gates = sorted(set(self.ci) - {"repository-qualification"})
        cases = [(gate, state) for gate in gates for state in ("failure", "skipped", "cancelled")]
        inputs = [" ".join(f"{gate}={state if gate == failed else 'success'}" for gate in gates)
                  for failed, state in cases]

        # Act
        results = [self.run_required_check(value) for value in inputs]

        # Assert
        for (gate, state), result in zip(cases, results, strict=True):
            with self.subTest(gate=gate, state=state):
                self.assertNotEqual(0, result.returncode)
                self.assertIn(f"{gate} concluded '{state}'.", result.stdout)

    def test_candidate_and_completion_artifacts_fail_if_missing(self):
        """Missing retained evidence cannot appear as a successful artifact upload."""
        # Arrange
        bodies = (self.ci["build"], self.ci["tests"], self.release["qualify"],
                  self.release["attest"], self.release["publish"])

        # Act
        uploads = [match.group(0) for body in bodies for match in
                   re.finditer(r"uses: actions/upload-artifact@.*?(?=\n      -|\Z)", body, re.DOTALL)]
        required_uploads = [upload for upload in uploads if "if-no-files-found: warn" not in upload]

        # Assert
        self.assertGreaterEqual(len(required_uploads), 4)
        self.assertTrue(all("if-no-files-found: error" in upload for upload in required_uploads))
        self.assertIn("if: always()", self.release["publish"])
        self.assertIn("retention-days: 30", self.release["publish"])


if __name__ == "__main__":
    unittest.main()

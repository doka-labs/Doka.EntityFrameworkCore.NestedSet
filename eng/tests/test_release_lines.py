"""Positive and rejection controls for exact release lines and their own toolchains."""

import copy
import json
import tempfile
import unittest
from pathlib import Path

from eng.release import lines
from eng.release.common import PACKAGES, REPOSITORY, ReleaseError


ROOT = Path(__file__).resolve().parents[2]
COMMIT = "a" * 40


def policy():
    """Provide independent current, maintenance and preview authority for boundary tests."""
    return {
        "schemaVersion": 1, "revision": "2026-10-28.1", "repository": REPOSITORY,
        "authorityRef": "refs/heads/main", "lines": [
            {"sourceRef": "refs/heads/main", "role": "main", "packageMajor": 10,
             "channel": "stable-and-prerelease", "upstreamStage": "ga", "state": "active",
             "workflowPath": ".github/workflows/release-candidate.yml", "publisherEnvironment": "nuget"},
            {"sourceRef": "refs/heads/maintenance/10.x", "role": "maintenance", "packageMajor": 10,
             "channel": "stable-and-prerelease", "upstreamStage": "ga", "state": "inactive",
             "workflowPath": ".github/workflows/release-candidate.yml", "publisherEnvironment": "nuget"},
            {"sourceRef": "refs/heads/preview/11.x", "role": "preview", "packageMajor": 11,
             "channel": "prerelease", "upstreamStage": "rc", "state": "inactive",
             "workflowPath": ".github/workflows/release-candidate.yml", "publisherEnvironment": "nuget"},
        ],
    }


def source(directory, major=10, preview=False):
    """Create the owned literal source facts and shipping locks without restoring packages."""
    (directory / "src").mkdir(exist_ok=True)
    sdk = f"{major}.0.403" if not preview else f"{major}.0.100-rc.2.26504.105"
    version = f"{major}.0.14" if not preview else f"{major}.0.0-rc.2.26504.105"
    (directory / "global.json").write_text(json.dumps({"sdk": {
        "version": sdk, "rollForward": "disable", "allowPrerelease": preview,
    }}))
    (directory / "Directory.Build.props").write_text(
        f"<Project><PropertyGroup><TargetFramework>net{major}.0</TargetFramework></PropertyGroup></Project>")
    (directory / "src/Directory.Build.props").write_text(
        f"<Project><PropertyGroup><VersionPrefix>{major}.1.0</VersionPrefix></PropertyGroup></Project>")
    (directory / "Directory.Packages.props").write_text(
        '<Project><ItemGroup><PackageVersion Include="Microsoft.EntityFrameworkCore" '
        f'Version="{version}"/><PackageVersion Include="Microsoft.EntityFrameworkCore.Relational" '
        f'Version="{version}"/><PackageVersion Include="Doka.EntityFrameworkCore.MySql" '
        f'Version="{version}"/></ItemGroup></Project>')
    for package in PACKAGES:
        path = directory / "src" / package
        path.mkdir()
        (path / "packages.lock.json").write_text(json.dumps({"version": 2, "dependencies": {
            f"net{major}.0": {"Microsoft.EntityFrameworkCore": {"type": "Direct", "resolved": version}},
        }}))


class PolicyTests(unittest.TestCase):
    """Require exact authority, branch ownership and activation without publishing anything."""

    def test_repository_policy_matches_initial_contract(self):
        # Arrange
        expected = policy()

        # Act
        actual = lines.load_policy()

        # Assert
        self.assertEqual(expected, actual)

    def test_policy_rejects_invalid_authority_or_schema(self):
        for name, value in (("schemaVersion", True), ("schemaVersion", 2), ("repository", "other/repo"),
                            ("authorityRef", "refs/heads/feature"), ("revision", ""),
                            ("lines", []), ("extra", True)):
            with self.subTest(name=name, value=value):
                # Arrange
                invalid = policy()
                invalid[name] = value

                # Act / Assert
                with self.assertRaises(ReleaseError):
                    lines.validate_policy(invalid)

    def test_duplicate_json_policy_fields_are_rejected(self):
        # Arrange
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        path = Path(temporary.name) / "policy.json"
        path.write_text('{"schemaVersion":1,"schemaVersion":1}')

        # Act / Assert
        with self.assertRaises(ReleaseError):
            lines.load_policy(path)

    def test_policy_rejects_invalid_line_identity_or_fields(self):
        cases = (("sourceRef", "refs/heads/maintenance/*"), ("sourceRef", "refs/tags/main"),
                 ("sourceRef", "refs/heads/feature/work"), ("role", "other"),
                 ("packageMajor", True), ("packageMajor", 0), ("state", "ready"),
                 ("channel", "anything"), ("upstreamStage", "preview"),
                 ("workflowPath", ".github/workflows/other.yml"), ("publisherEnvironment", "other"),
                 ("extra", "ignored"))
        for name, value in cases:
            with self.subTest(name=name, value=value):
                # Arrange
                invalid = policy()
                invalid["lines"][0][name] = value

                # Act / Assert
                with self.assertRaises(ReleaseError):
                    lines.validate_policy(invalid)

    def test_duplicate_refs_and_active_major_owners_are_rejected(self):
        for duplicate_ref in (False, True):
            with self.subTest(duplicate_ref=duplicate_ref):
                # Arrange
                invalid = policy()
                if duplicate_ref:
                    invalid["lines"].append(copy.deepcopy(invalid["lines"][0]))
                else:
                    invalid["lines"][1]["state"] = "active"

                # Act / Assert
                with self.assertRaises(ReleaseError):
                    lines.validate_policy(invalid)

    def test_registered_inactive_branch_allows_preparation_pr_only(self):
        # Arrange
        authority = policy()
        reference = "refs/heads/maintenance/10.x"

        # Act
        selected = lines.select_line(authority, reference, purpose="pr")

        # Assert
        self.assertEqual("inactive", selected["state"])

    def test_inactive_line_rejects_qualification_publication_and_scheduling(self):
        for purpose in ("qualify", "publish", "maintenance"):
            with self.subTest(purpose=purpose):
                # Arrange
                authority = policy()

                # Act / Assert
                with self.assertRaises(ReleaseError):
                    lines.select_line(authority, "refs/heads/maintenance/10.x", purpose=purpose)

    def test_prepared_line_qualifies_without_publication_or_scheduling(self):
        # Arrange
        authority = policy()
        authority["lines"][1]["state"] = "prepared"
        reference = "refs/heads/maintenance/10.x"

        # Act
        snapshot = lines.freeze_line(authority, reference, "10.1.0", policy_commit=COMMIT,
                                     qualification_only=True)

        # Assert
        self.assertTrue(snapshot["qualificationOnly"])
        self.assertEqual("prepared", snapshot["state"])

    def test_prepared_line_rejects_publication_and_scheduling(self):
        for purpose in ("publish", "maintenance"):
            with self.subTest(purpose=purpose):
                # Arrange
                authority = policy()
                authority["lines"][1]["state"] = "prepared"

                # Act / Assert
                with self.assertRaises(ReleaseError):
                    lines.select_line(authority, "refs/heads/maintenance/10.x", "10.1.0", purpose=purpose)

    def test_active_major_transfer_preserves_independent_lines(self):
        # Arrange
        authority = policy()
        authority["lines"][0]["packageMajor"] = 11
        authority["lines"][1]["state"] = "active"

        # Act
        current = lines.select_line(authority, "refs/heads/main", "11.0.0", purpose="publish")
        maintenance = lines.select_line(authority, "refs/heads/maintenance/10.x", "10.1.1", purpose="publish")

        # Assert
        self.assertEqual(11, current["packageMajor"])
        self.assertEqual(10, maintenance["packageMajor"])

    def test_unknown_purpose_ref_wrong_major_and_noncanonical_version_are_rejected(self):
        cases = (("refs/heads/main", "10.1.0", "other"),
                 ("refs/heads/feature/work", "10.1.0", "pr"),
                 ("refs/heads/main", "11.0.0", "publish"),
                 ("refs/heads/main", "10.01.0", "publish"),
                 ("refs/heads/main", "10.1.0+metadata", "publish"))
        for reference, version, purpose in cases:
            with self.subTest(reference=reference, version=version, purpose=purpose):
                # Arrange
                authority = policy()

                # Act / Assert
                with self.assertRaises(ReleaseError):
                    lines.select_line(authority, reference, version, purpose=purpose)

    def test_preview_accepts_numbered_rc_but_never_stable(self):
        # Arrange
        authority = policy()
        authority["lines"][2]["state"] = "active"

        # Act
        selected = lines.select_line(authority, "refs/heads/preview/11.x", "11.0.0-rc.1", purpose="publish")

        # Assert
        self.assertEqual("rc", selected["upstreamStage"])

    def test_preview_rejects_stable_publication(self):
        # Arrange
        authority = policy()
        authority["lines"][2]["state"] = "active"

        # Act / Assert
        with self.assertRaises(ReleaseError):
            lines.select_line(authority, "refs/heads/preview/11.x", "11.0.0", purpose="publish")

    def test_every_line_can_be_retired_without_losing_registration(self):
        # Arrange
        authority = policy()
        for entry in authority["lines"]:
            entry["state"] = "inactive"

        # Act
        actual = lines.validate_policy(authority)

        # Assert
        self.assertEqual(3, len(actual["lines"]))
        self.assertFalse(any(entry["state"] == "active" for entry in actual["lines"]))

    def test_digest_is_deterministic_and_changes_with_authority(self):
        # Arrange
        original = policy()
        reordered = dict(reversed(list(original.items())))
        changed = copy.deepcopy(original)
        changed["revision"] = "2026-10-28.2"

        # Act
        actual = (lines.policy_digest(original), lines.policy_digest(reordered), lines.policy_digest(changed))

        # Assert
        self.assertEqual(actual[0], actual[1])
        self.assertNotEqual(actual[0], actual[2])

    def test_snapshot_binds_authority_source_major_and_qualification_mode(self):
        # Arrange
        authority = policy()

        # Act
        snapshot = lines.freeze_line(authority, "refs/heads/main", "10.1.0-rc.1", policy_commit=COMMIT)
        authority["lines"][0]["state"] = "inactive"
        actual = lines.validate_snapshot(snapshot, source_ref="refs/heads/main", version="10.1.0-rc.1")

        # Assert
        self.assertEqual("active", actual["state"])
        self.assertEqual(COMMIT, actual["authorityCommit"])
        self.assertFalse(actual["qualificationOnly"])
        self.assertEqual(64, len(actual["policySha256"]))

    def test_snapshot_tampering_is_rejected(self):
        cases = (("schemaVersion", True), ("repository", "other/repo"), ("authorityCommit", "a" * 39),
                 ("policySha256", "x" * 64), ("sourceRef", "refs/heads/main/../other"),
                 ("state", "inactive"), ("qualificationOnly", "false"), ("extra", True))
        for name, value in cases:
            with self.subTest(name=name, value=value):
                # Arrange
                snapshot = lines.freeze_line(policy(), "refs/heads/main", "10.1.0", policy_commit=COMMIT)
                snapshot[name] = value

                # Act / Assert
                with self.assertRaises(ReleaseError):
                    lines.validate_snapshot(snapshot, source_ref="refs/heads/main", version="10.1.0")

    def test_prepared_snapshot_cannot_claim_publication(self):
        # Arrange
        authority = policy()
        authority["lines"][1]["state"] = "prepared"
        snapshot = lines.freeze_line(authority, "refs/heads/maintenance/10.x", "10.1.0",
                                     policy_commit=COMMIT, qualification_only=True)
        snapshot["qualificationOnly"] = False

        # Act / Assert
        with self.assertRaises(ReleaseError):
            lines.validate_snapshot(snapshot)


class ToolchainTests(unittest.TestCase):
    """Inspect literal owned source and actual shipping lock resolutions, not another line's SDK."""

    def setUp(self):
        """Create one independent source tree and its previously observed exact-line authority."""
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.directory = Path(self.temporary.name)
        source(self.directory)
        self.snapshot = lines.freeze_line(policy(), "refs/heads/main", "10.1.0", policy_commit=COMMIT)

    def test_current_repository_and_independent_ga_fixture_are_accepted(self):
        # Arrange
        sources = [ROOT, self.directory]

        # Act
        actual = [lines.inspect_toolchain(path, self.snapshot) for path in sources]

        # Assert
        self.assertTrue(all(item["targetFramework"] == "net10.0" for item in actual))
        self.assertTrue(all(item["sdkVersion"].startswith("10.") for item in actual))

    def test_source_framework_or_package_version_from_another_major_is_rejected(self):
        cases = (("Directory.Build.props", "net10.0", "net11.0"),
                 ("src/Directory.Build.props", "10.1.0", "11.1.0"),
                 ("Directory.Packages.props", 'Version="10.0.14"', 'Version="11.0.0"'))
        for name, old, new in cases:
            with self.subTest(name=name):
                # Arrange
                path = self.directory / name
                original = path.read_text()
                path.write_text(original.replace(old, new))

                # Act / Assert
                with self.assertRaises(ReleaseError):
                    lines.inspect_toolchain(self.directory, self.snapshot)

                path.write_text(original)

    def test_ga_sdk_requires_exact_major_and_explicit_ga_selection(self):
        cases = (("version", "11.0.403"), ("version", "10.0.100-rc.1.12345.1"),
                 ("rollForward", "latestMajor"), ("allowPrerelease", True))
        for name, value in cases:
            with self.subTest(name=name, value=value):
                # Arrange
                original = json.loads((self.directory / "global.json").read_text())
                invalid = copy.deepcopy(original)
                invalid["sdk"][name] = value
                (self.directory / "global.json").write_text(json.dumps(invalid))

                # Act / Assert
                with self.assertRaises(ReleaseError):
                    lines.inspect_toolchain(self.directory, self.snapshot)

                (self.directory / "global.json").write_text(json.dumps(original))

    def test_ga_rejects_resolved_foreign_major_and_prerelease_dependencies(self):
        path = self.directory / "src" / PACKAGES[1] / "packages.lock.json"
        for version in ("11.0.0", "10.0.0-rc.1.12345.1"):
            with self.subTest(version=version):
                # Arrange
                lock = json.loads(path.read_text())
                lock["dependencies"]["net10.0"]["Microsoft.EntityFrameworkCore"]["resolved"] = version
                path.write_text(json.dumps(lock))

                # Act / Assert
                with self.assertRaises(ReleaseError):
                    lines.inspect_toolchain(self.directory, self.snapshot)

    def test_unbounded_and_cross_major_ranges_are_rejected(self):
        path = self.directory / "Directory.Packages.props"
        for version in ("10.*", "[10.0.0,)", "[9.0.0,11.0.0)", "[10.0.0,12.0.0)"):
            with self.subTest(version=version):
                # Arrange
                original = path.read_text()
                path.write_text(original.replace('Version="10.0.14"', f'Version="{version}"'))

                # Act / Assert
                with self.assertRaises(ReleaseError):
                    lines.inspect_toolchain(self.directory, self.snapshot)

                path.write_text(original)

    def test_owned_compatibility_range_and_independent_tool_versions_are_accepted(self):
        # Arrange
        path = self.directory / "Directory.Packages.props"
        text = path.read_text().replace('Version="10.0.14"', 'Version="[10.0.12,11.0.0)"')
        text = text.replace('</ItemGroup>', '<PackageVersion Include="Microsoft.NET.Test.Sdk" '
                            'Version="18.11.0"/><PackageVersion Include="BenchmarkDotNet" '
                            'Version="0.15.8"/></ItemGroup>')
        path.write_text(text)

        # Act
        actual = lines.inspect_toolchain(self.directory, self.snapshot)

        # Assert
        self.assertEqual("net10.0", actual["targetFramework"])

    def test_registered_preview_requires_its_own_rc_sdk_and_graph(self):
        # Arrange
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        preview = Path(temporary.name)
        source(preview, major=11, preview=True)
        authority = policy()
        authority["lines"][2]["state"] = "prepared"
        snapshot = lines.freeze_line(authority, "refs/heads/preview/11.x", "11.0.0-rc.1",
                                     policy_commit=COMMIT, qualification_only=True)

        # Act
        actual = lines.inspect_toolchain(preview, snapshot)

        # Assert
        self.assertEqual("net11.0", actual["targetFramework"])
        self.assertIn("-rc.", actual["sdkVersion"])

    def test_preview_does_not_silently_accept_ga_dependencies(self):
        # Arrange
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        preview = Path(temporary.name)
        source(preview, major=11, preview=True)
        authority = policy()
        authority["lines"][2]["state"] = "prepared"
        snapshot = lines.freeze_line(authority, "refs/heads/preview/11.x", "11.0.0-rc.1",
                                     policy_commit=COMMIT, qualification_only=True)
        path = preview / "Directory.Packages.props"
        path.write_text(path.read_text().replace("11.0.0-rc.2.26504.105", "11.0.0"))

        # Act / Assert
        with self.assertRaises(ReleaseError):
            lines.inspect_toolchain(preview, snapshot)

    def test_preview_compatibility_range_may_end_at_its_own_ga_version(self):
        # Arrange
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        preview = Path(temporary.name)
        source(preview, major=11, preview=True)
        authority = policy()
        authority["lines"][2]["state"] = "prepared"
        snapshot = lines.freeze_line(authority, "refs/heads/preview/11.x", "11.0.0-rc.1",
                                     policy_commit=COMMIT, qualification_only=True)
        path = preview / "Directory.Packages.props"
        path.write_text(path.read_text().replace('Version="11.0.0-rc.2.26504.105"',
                                               'Version="[11.0.0-rc.2.26504.105,11.0.0)"'))

        # Act
        actual = lines.inspect_toolchain(preview, snapshot)

        # Assert
        self.assertEqual("rc", actual["upstreamStage"])

    def test_malformed_sdk_or_lock_objects_are_rejected(self):
        for name in ("global.json", f"src/{PACKAGES[0]}/packages.lock.json"):
            with self.subTest(name=name):
                # Arrange
                path = self.directory / name
                original = path.read_text()
                path.write_text("[]")

                # Act / Assert
                with self.assertRaises(ReleaseError):
                    lines.inspect_toolchain(self.directory, self.snapshot)

                path.write_text(original)

    def test_source_framework_cannot_be_hidden_behind_a_condition(self):
        # Arrange
        path = self.directory / "Directory.Build.props"
        path.write_text(path.read_text().replace("<PropertyGroup>", '<PropertyGroup Condition="false">'))

        # Act / Assert
        with self.assertRaises(ReleaseError):
            lines.inspect_toolchain(self.directory, self.snapshot)

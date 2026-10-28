"""Exercise frozen line identity through the real qualifier, seal and candidate reader."""

import copy
import io
import json
import os
import zipfile
from contextlib import redirect_stdout

from eng.release import common, lines, qualification
from eng.tests.test_release_lines import policy
from eng.tests.test_release_qualification import COMMIT, VERSION, QualificationFixture, capture, stage_fixture


class ReleaseLineQualificationTests(QualificationFixture):
    """Keep line classification and exact-attempt restoration attached to actual candidate assembly."""

    def select(self, reference, *, authority=None, qualification_only=False):
        """Select an independently observed frozen grant without replacing qualifier or candidate code."""
        snapshot = lines.freeze_line(authority or policy(), reference, VERSION, policy_commit=COMMIT,
                                     qualification_only=qualification_only)
        os.environ["GITHUB_REF"] = reference
        os.environ["GITHUB_WORKFLOW_REF"] = f"{common.REPOSITORY}/{common.RELEASE_WORKFLOW}@{reference}"
        os.environ["NESTEDSET_RELEASE_LINE"] = json.dumps(snapshot)

        return snapshot

    def seal(self, runner):
        """Seal prearranged synthetic bytes with the unchanged real assembly and loading paths."""
        with redirect_stdout(io.StringIO()):
            runner.assemble()

        return common.load_candidate(runner.output / "candidate", require_publishable=False)

    def test_active_current_line_seals_schema_two_with_toolchain_and_six_packages(self):
        """The existing candidate carries the frozen line and exact producer facts without another receipt."""
        # Arrange
        stage_fixture(self.runner)

        # Act
        manifest = self.seal(self.runner)

        # Assert
        self.assertEqual(2, manifest["schemaVersion"])
        self.assertTrue(manifest["publishable"])
        self.assertEqual("refs/heads/main", manifest["line"]["sourceRef"])
        self.assertEqual(self.runner.identity["line"], manifest["line"])
        self.assertEqual(self.runner.identity["toolchain"], manifest["toolchain"])
        self.assertEqual("net10.0", manifest["toolchain"]["targetFramework"])
        self.assertEqual(list(common.STAGES), manifest["stages"])
        self.assertEqual(12, sum(name.endswith((".nupkg", ".snupkg")) for name in manifest["files"]))
        with zipfile.ZipFile(self.runner.output / "candidate/qualification-evidence.zip") as archive:
            embedded = json.loads(archive.read("identity.json"))

        self.assertEqual(self.runner.identity, embedded)

    def test_prepared_line_seals_complete_evidence_without_publication(self):
        """Prepared maintenance qualification keeps every stage while remaining nonpublishable."""
        # Arrange
        authority = policy()
        authority["lines"][1]["state"] = "prepared"
        self.select("refs/heads/maintenance/10.x", authority=authority, qualification_only=True)
        runner = qualification.Qualification(self.repo, self.root / "prepared", VERSION)
        stage_fixture(runner)

        # Act
        manifest = self.seal(runner)

        # Assert
        self.assertEqual(2, manifest["schemaVersion"])
        self.assertFalse(manifest["publishable"])
        self.assertEqual("prepared", manifest["line"]["state"])
        self.assertTrue(manifest["line"]["qualificationOnly"])
        self.assertEqual(list(common.STAGES), manifest["stages"])
        self.assertEqual(self.runner.identity["toolchain"], manifest["toolchain"])

    def test_active_qualification_only_seals_without_publication(self):
        """An active line's nonpublishing rehearsal cannot inherit its ordinary publication classification."""
        # Arrange
        self.select("refs/heads/main", qualification_only=True)
        runner = qualification.Qualification(self.repo, self.root / "active-rehearsal", VERSION)
        stage_fixture(runner)

        # Act
        manifest = self.seal(runner)

        # Assert
        self.assertEqual("active", manifest["line"]["state"])
        self.assertTrue(manifest["line"]["qualificationOnly"])
        self.assertFalse(manifest["publishable"])

    def test_qualification_only_candidate_cannot_be_loaded_for_publication(self):
        """Complete evidence is not publication authority when the operator selected qualification only."""
        # Arrange
        self.select("refs/heads/main", qualification_only=True)
        runner = qualification.Qualification(self.repo, self.root / "nonpublishing", VERSION)
        stage_fixture(runner)
        self.seal(runner)

        # Act
        error = capture(lambda: common.load_candidate(runner.output / "candidate"))

        # Assert
        self.assertIsInstance(error, common.ReleaseError)
        self.assertEqual("workspace_only", error.code)

    def test_main_eleven_authority_allows_ten_maintenance_with_its_own_source(self):
        """Changing main's major does not impose its SDK or dependencies on the ten maintenance branch."""
        # Arrange
        authority = policy()
        authority["lines"][0]["packageMajor"] = 11
        authority["lines"][1]["state"] = "active"
        self.select("refs/heads/maintenance/10.x", authority=authority)
        runner = qualification.Qualification(self.repo, self.root / "maintenance", VERSION)
        stage_fixture(runner)

        # Act
        manifest = self.seal(runner)

        # Assert
        self.assertTrue(manifest["publishable"])
        self.assertEqual("refs/heads/maintenance/10.x", manifest["workflow"]["ref"])
        self.assertEqual(10, manifest["line"]["packageMajor"])
        self.assertEqual("10.0.403", manifest["toolchain"]["sdkVersion"])
        self.assertEqual("net10.0", manifest["toolchain"]["targetFramework"])

    def test_missing_frozen_environment_is_rejected_before_output_creation(self):
        """A hosted workflow cannot fall back to unauthenticated workspace qualification."""
        # Arrange
        os.environ.pop("NESTEDSET_RELEASE_LINE")
        output = self.root / "missing-grant"

        # Act
        error = capture(lambda: qualification.Qualification(self.repo, output, VERSION))

        # Assert
        self.assertIsInstance(error, common.ReleaseError)
        self.assertEqual("line_identity", error.code)
        self.assertFalse(output.exists())

    def test_duplicate_frozen_json_fields_are_rejected(self):
        """The hosted identity parser cannot choose one of two contradictory authority fields."""
        # Arrange
        original = os.environ["NESTEDSET_RELEASE_LINE"]
        os.environ["NESTEDSET_RELEASE_LINE"] = original.replace(
            '"qualificationOnly": false', '"qualificationOnly": false, "qualificationOnly": true')
        output = self.root / "duplicate-grant"

        # Act
        error = capture(lambda: qualification.Qualification(self.repo, output, VERSION))

        # Assert
        self.assertIsInstance(error, common.ReleaseError)
        self.assertEqual("json-invalid", error.code)
        self.assertFalse(output.exists())

    def test_frozen_grant_for_another_workflow_ref_is_rejected(self):
        """A branch-native workflow cannot reuse main's frozen grant merely by changing its hosted ref."""
        # Arrange
        reference = "refs/heads/maintenance/10.x"
        os.environ["GITHUB_REF"] = reference
        os.environ["GITHUB_WORKFLOW_REF"] = f"{common.REPOSITORY}/{common.RELEASE_WORKFLOW}@{reference}"

        # Act
        error = capture(lambda: qualification.Qualification(self.repo, self.root / "wrong-ref", VERSION))

        # Assert
        self.assertIsInstance(error, common.ReleaseError)
        self.assertEqual("line-snapshot", error.code)

    def test_source_framework_or_sdk_from_another_line_is_rejected(self):
        """Frozen permission never authorizes source built with the other framework major."""
        cases = (("Directory.Build.props", "net10.0", "net11.0"),
                 ("global.json", "10.0.403", "11.0.403"))
        for index, (name, old, new) in enumerate(cases):
            with self.subTest(name=name):
                # Arrange
                path = self.repo / name
                original = path.read_text()
                path.write_text(original.replace(old, new))

                # Act
                error = capture(lambda: qualification.Qualification(self.repo, self.root / f"wrong-source-{index}",
                                                                   VERSION))

                # Assert
                self.assertIsInstance(error, common.ReleaseError)
                self.assertEqual("line-toolchain", error.code)
                path.write_text(original)

    def test_restored_build_keeps_original_frozen_authority_and_mode(self):
        """Even a syntactically valid replacement grant cannot change a downloaded build's identity."""
        cases = (("policySha256", "d" * 64), ("authorityCommit", "d" * 40), ("qualificationOnly", True))
        for name, value in cases:
            with self.subTest(name=name):
                # Arrange
                snapshot = copy.deepcopy(self.runner.line)
                snapshot[name] = value
                os.environ["NESTEDSET_RELEASE_LINE"] = json.dumps(snapshot)

                # Act
                error = capture(lambda: qualification.Qualification(self.repo, self.runner.output, VERSION,
                                                                   existing=True))

                # Assert
                self.assertIsInstance(error, common.ReleaseError)
                self.assertEqual("build_identity", error.code)

    def test_schema_two_tampering_is_rejected_by_candidate_reader(self):
        """The sealed manifest's typed major, source ref and publication classification remain mandatory."""
        candidate = self.complete_fixture()
        path = candidate / "candidate.json"
        original = common.read_json(path)
        cases = (("packageMajor", 11), ("packageMajor", True),
                 ("sourceRef", "refs/heads/maintenance/10.x"), ("publishable", False))
        for name, value in cases:
            with self.subTest(name=name, value=value):
                # Arrange
                manifest = copy.deepcopy(original)
                if name == "publishable":
                    manifest[name] = value
                else:
                    manifest["line"][name] = value

                common.write_json(path, manifest)

                # Act
                error = capture(lambda: common.load_candidate(candidate))

                # Assert
                self.assertIsInstance(error, common.ReleaseError)
                self.assertEqual("line_identity", error.code)

    def test_historical_main_schema_one_binding_survives_later_line_policy(self):
        """Historical evidence retains its original main ref without consulting today's package major."""
        # Arrange
        candidate = self.complete_fixture()
        path = candidate / "candidate.json"
        historical = common.read_json(path)
        historical["schemaVersion"] = 1
        historical.pop("line")
        historical.pop("toolchain")
        common.write_json(path, historical)
        future = policy()
        future["lines"][0]["packageMajor"] = 11
        future["lines"][1]["state"] = "active"
        self.select("refs/heads/maintenance/10.x", authority=future)

        # Act
        manifest = common.load_candidate(candidate)

        # Assert
        self.assertEqual(1, manifest["schemaVersion"])
        self.assertEqual("refs/heads/main", common.candidate_source_ref(manifest))
        self.assertEqual(VERSION, manifest["version"])

    def test_historical_schema_one_cannot_be_reassigned_to_maintenance(self):
        """Legacy main evidence cannot acquire another source identity from a later maintenance grant."""
        # Arrange
        candidate = self.complete_fixture()
        path = candidate / "candidate.json"
        historical = common.read_json(path)
        historical["schemaVersion"] = 1
        historical.pop("line")
        historical.pop("toolchain")
        historical["workflow"]["ref"] = "refs/heads/maintenance/10.x"
        common.write_json(path, historical)

        # Act
        error = capture(lambda: common.load_candidate(candidate))

        # Assert
        self.assertIsInstance(error, common.ReleaseError)
        self.assertEqual("workflow_identity", error.code)

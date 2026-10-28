"""Monotone stable Latest promotion within the shared publication boundary."""

import unittest

from eng.release import lines
from eng.release import publication as release
from eng.release.common import write_json
from eng.tests import test_release_publication as baseline


class LatestTests(unittest.TestCase):
    """Run actual existing draft/readback publication with independent prior Latest state."""

    def setUp(self):
        """Prepare stable exact candidate bytes and retain real fixture cleanup."""
        self.case = baseline.PublicationTests()
        self.case.release_fixture_version = "10.1.1"
        self.case.setUp()
        self.addCleanup(self.case.doCleanups)

    def latest(self, version, *, release_id=99, commit="e" * 40):
        """Expose a different immutable signed stable release as current Latest."""
        hosted = self.case.hosted
        tag = "v" + version
        tag_object = "b" * 40 if version == self.case.version else "f" * 40
        hosted.latest = {"id": release_id, "tag_name": tag, "draft": False, "prerelease": False, "immutable": True}
        hosted.api_overrides[f"git/ref/tags/{tag}"] = {"object": {"type": "tag", "sha": tag_object}}
        hosted.api_overrides[f"git/tags/{tag_object}"] = {
            "tag": tag, "object": {"type": "commit", "sha": commit},
            "verification": {"verified": True, "reason": "valid"}}

    def test_newer_stable_promotes_after_complete_readback(self):
        """A genuinely newer patch can replace an older signed stable Latest."""
        # Arrange
        self.latest("10.1.0")
        self.case.hosted.existing_release()

        # Act
        result = self.case.publish()

        # Assert
        self.assertEqual("published", result["state"])
        publication = [call for call in self.case.hosted.writes if call[2] == "edit"]
        self.assertEqual(1, len(publication))
        self.assertIn("--latest=true", publication[0])
        self.assertEqual("v10.1.1", self.case.hosted.latest["tag_name"])

    def test_first_stable_promotes_only_after_actual_not_found(self):
        """An authenticated empty Latest endpoint admits the first stable release."""
        # Arrange
        self.case.hosted.existing_release()

        # Act
        result = self.case.publish()

        # Assert
        self.assertEqual("published", result["state"])
        self.assertEqual("v10.1.1", self.case.hosted.latest["tag_name"])

    def test_successful_edit_without_observed_promotion_is_not_complete(self):
        """A successful command alone cannot prove the resulting GitHub Latest state."""
        # Arrange
        self.latest("10.1.0")
        self.case.hosted.existing_release()
        self.case.hosted.ignore_latest = True

        # Act
        error = baseline.record_error(self.case.publish)

        # Assert
        self.assertRegex(str(error), "release-latest")
        self.assertFalse(self.case.output.exists())

    def test_late_older_patch_publishes_without_displacing_latest(self):
        """Completion order does not turn a lower patch into GitHub Latest."""
        # Arrange
        self.latest("10.1.2")
        self.case.hosted.existing_release()

        # Act
        result = self.case.publish()

        # Assert
        self.assertEqual("published", result["state"])
        self.assertEqual("v10.1.2", self.case.hosted.latest["tag_name"])
        publication = [call for call in self.case.hosted.writes if call[2] == "edit"]
        self.assertIn("--latest=false", publication[0])

    def test_maintenance_stays_stable_after_next_major_is_latest(self):
        """The old framework line remains publishable without becoming Latest."""
        # Arrange
        case = self.case
        main = case.hosted.policy["lines"][0]
        maintenance = {**main, "role": "maintenance", "sourceRef": "refs/heads/maintenance/10.x"}
        main["packageMajor"] = 11
        case.hosted.policy["lines"].append(maintenance)
        case.hosted.branch = "maintenance/10.x"
        case.manifest.update(schemaVersion=2)
        case.manifest["workflow"]["ref"] = maintenance["sourceRef"]
        case.manifest["line"] = lines.freeze_line(case.hosted.policy, maintenance["sourceRef"], case.version,
                                                 policy_commit=case.hosted.authority_commit)
        workflow_ref = f"{release.REPOSITORY}/{release.WORKFLOW}@{maintenance['sourceRef']}"
        case.environment.update(GITHUB_REF=maintenance["sourceRef"],
                                GITHUB_WORKFLOW_REF=workflow_ref)
        write_json(case.candidate / "candidate.json", case.manifest)
        case.write_bundle("release-provenance.jsonl", case.make_statement())
        case.hosted.existing_release()
        self.latest("11.0.0")

        # Act
        result = case.publish()

        # Assert
        self.assertEqual("published", result["state"])
        self.assertFalse(case.hosted.releases[0]["prerelease"])
        self.assertEqual("v11.0.0", case.hosted.latest["tag_name"])
        self.assertIn("--latest=false", [call for call in case.hosted.writes if call[2] == "edit"][0])

    def test_historical_completion_does_not_require_continued_latest(self):
        """Immutable content can be reverified after a higher release becomes Latest."""
        # Arrange
        self.case.hosted.existing_release(draft=False)
        self.latest("11.0.0")

        # Act
        result = release.complete(self.case.candidate, self.case.provenance, self.case.readback,
                                  self.case.output, **self.case.options)

        # Assert
        self.assertEqual("complete", result["state"])
        self.assertEqual([], self.case.hosted.writes)

    def test_invalid_latest_metadata_cannot_be_treated_as_no_release(self):
        """An unknown or mutable release blocks promotion rather than failing open."""
        # Arrange
        self.latest("10.1.0")
        self.case.hosted.latest["immutable"] = False
        self.case.hosted.existing_release()

        # Act
        error = baseline.record_error(self.case.publish)

        # Assert
        self.assertRegex(str(error), "latest-invalid")
        self.assertEqual([], self.case.hosted.writes)

    def test_unverified_latest_tag_blocks_promotion(self):
        """A display version cannot establish release ordering without its verified source tag."""
        # Arrange
        self.latest("10.1.0")
        self.case.hosted.api_overrides[f"git/tags/{'f' * 40}"]["verification"]["verified"] = False
        self.case.hosted.existing_release()

        # Act
        error = baseline.record_error(self.case.publish)

        # Assert
        self.assertRegex(str(error), "latest-tag")
        self.assertEqual([], self.case.hosted.writes)

    def test_equal_version_with_foreign_release_or_source_is_rejected(self):
        """An equal tag cannot authorize a conflicting release ID or commit."""
        # Arrange
        self.latest("10.1.1", commit=self.case.manifest["source"]["commit"])
        self.case.hosted.existing_release()

        # Act
        error = baseline.record_error(self.case.publish)

        # Assert
        self.assertRegex(str(error), "latest-conflict")
        self.assertEqual([], self.case.hosted.writes)

    def test_immutable_retry_does_not_promote_again(self):
        """An already completed release is verified without another public state mutation."""
        # Arrange
        self.case.hosted.existing_release(draft=False)
        self.latest("11.0.0")

        # Act
        result = self.case.publish()

        # Assert
        self.assertEqual("published", result["state"])
        self.assertEqual([], self.case.hosted.writes)
        self.assertEqual("v11.0.0", self.case.hosted.latest["tag_name"])

    def test_equal_immutable_source_keeps_its_existing_latest_identity(self):
        """The same completed candidate needs no new promotion decision or replacement identity."""
        # Arrange
        self.latest(self.case.version, release_id=41, commit=self.case.manifest["source"]["commit"])
        existing = {"id": 41, "draft": False, "immutable": True}

        # Act
        promote = release.latest_promotion(self.case.hosted, self.case.manifest, existing)

        # Assert
        self.assertFalse(promote)
        self.assertEqual([], self.case.hosted.writes)

    def test_equal_latest_cannot_also_be_a_mutable_draft(self):
        """Contradictory remote draft/Latest state does not grant idempotent publication."""
        # Arrange
        self.latest(self.case.version, release_id=41, commit=self.case.manifest["source"]["commit"])
        draft = {"id": 41, "draft": True, "immutable": False}

        # Act
        error = baseline.record_error(lambda: release.latest_promotion(self.case.hosted, self.case.manifest, draft))

        # Assert
        self.assertRegex(str(error), "latest-conflict")
        self.assertEqual([], self.case.hosted.writes)


if __name__ == "__main__":
    unittest.main()

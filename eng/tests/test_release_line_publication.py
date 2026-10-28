"""Adversarial current-authority and source-line publication controls."""

import json
import unittest
from unittest.mock import Mock, patch

from eng.release import publication as release
from eng.release import lines
from eng.release.common import write_json
from eng.tests import test_release_publication as baseline


class ResponseCommands:
    """Expose only an authenticated CLI response, without invoking GitHub."""

    def __init__(self, status, body):
        self.status = status
        self.body = body
        self.calls = []

    def run(self, arguments, **options):
        """Retain the exact HTTP request and return its supplied status/body."""
        self.calls.append((arguments, options))

        return f"HTTP/2.0 {self.status}\r\ncontent-type: application/json\r\n\r\n{self.body}"


class OptionalApiTests(unittest.TestCase):
    """Only an authenticated Not Found response can mean missing authority or Latest."""

    def test_actual_not_found_can_be_reported_as_absent(self):
        """The first release or policy bootstrap may observe a real missing object."""
        # Arrange
        commands = ResponseCommands(404, json.dumps({"message": "Not Found"}))

        # Act
        result = release.api(commands, "releases/latest", allow_missing=True)

        # Assert
        self.assertIsNone(result)
        self.assertIn("--include", commands.calls[0][0])

    def test_permission_failure_is_not_absence_even_when_body_mentions_404(self):
        """A permission or transport failure cannot grant first-release promotion."""
        # Arrange
        commands = ResponseCommands(403, json.dumps({"message": "404 is mentioned but access is forbidden"}))

        # Act
        error = baseline.record_error(lambda: release.api(commands, "releases/latest", allow_missing=True))

        # Assert
        self.assertRegex(str(error), "api-status")

    def test_success_retains_the_actual_object(self):
        """An existing object keeps its authenticated payload for further validation."""
        # Arrange
        value = {"id": 41, "tag_name": "v10.1.0"}
        commands = ResponseCommands(200, json.dumps(value))

        # Act
        result = release.api(commands, "releases/latest", allow_missing=True)

        # Assert
        self.assertEqual(value, result)

    def test_nested_dispatch_inputs_use_structured_stdin(self):
        """Workflow inputs are JSON values rather than shell-interpreted command text."""
        # Arrange
        commands = Mock()
        commands.run.return_value = '{"workflow_run_id":91}'
        payload = {"ref": "maintenance/10.x", "inputs": {"maintenance": "true", "schedule-source-sha": "a" * 40}}

        # Act
        result = release.api_request(commands, "actions/workflows/ci.yml/dispatches", "POST", payload)

        # Assert
        self.assertEqual(91, result["workflow_run_id"])
        arguments, options = commands.run.call_args
        self.assertIn("--input", arguments[0])
        self.assertEqual(payload, json.loads(options["input_text"]))

    def test_not_found_body_without_http_status_is_not_absence(self):
        """An unauthenticated text fragment cannot stand in for the API's real status metadata."""
        # Arrange
        commands = Mock()
        commands.run.return_value = '{"message":"Not Found"}'

        # Act
        error = baseline.record_error(lambda: release.api(commands, "releases/latest", allow_missing=True))

        # Assert
        self.assertRegex(str(error), "api-status")


class LinePublicationTests(unittest.TestCase):
    """Exercise real candidate/attestation fixtures against mutable current line authority."""

    def setUp(self):
        """Reuse the existing signed-byte fixture without inheriting its entire test suite."""
        self.case = baseline.PublicationTests()
        self.case.setUp()
        self.addCleanup(self.case.doCleanups)

    def maintenance(self, *, state="active", qualification_only=False):
        """Move the same exact major's source to a separately authorized maintenance ref."""
        case = self.case
        main = case.hosted.policy["lines"][0]
        maintenance = {**main, "role": "maintenance", "sourceRef": "refs/heads/maintenance/1.x", "state": state}
        main["packageMajor"] = 2
        case.hosted.policy["lines"].append(maintenance)
        case.hosted.branch = "maintenance/1.x"
        case.manifest.update(schemaVersion=2, publishable=not qualification_only)
        case.manifest["workflow"]["ref"] = maintenance["sourceRef"]
        case.manifest["line"] = lines.freeze_line(case.hosted.policy, maintenance["sourceRef"], case.version,
                                                 policy_commit=case.hosted.authority_commit,
                                                 qualification_only=qualification_only)
        workflow_ref = f"{release.REPOSITORY}/{release.WORKFLOW}@{maintenance['sourceRef']}"
        case.environment.update(GITHUB_REF=maintenance["sourceRef"],
                                GITHUB_WORKFLOW_REF=workflow_ref)
        self.seal()

    def seal(self):
        """Bind modified fixture identity to its fresh fake signature subjects."""
        case = self.case
        write_json(case.candidate / "candidate.json", case.manifest)
        case.statement = case.make_statement()
        case.write_bundle("release-provenance.jsonl", case.statement)

    def test_maintenance_uses_its_original_source_and_provenance(self):
        """A maintained major qualifies independently while main owns the next major."""
        # Arrange
        self.maintenance()

        # Act
        result = self.case.stage()

        # Assert
        self.assertEqual("staged", result["state"])
        verification = [call for call in self.case.hosted.calls if call[:3] == ["gh", "attestation", "verify"]]
        self.assertTrue(verification)
        self.assertTrue(all(call[call.index("--source-ref") + 1] == "refs/heads/maintenance/1.x"
                            for call in verification))

    def test_revoked_current_line_blocks_original_qualified_bytes(self):
        """Frozen permission is historical evidence rather than perpetual publication authority."""
        # Arrange
        self.maintenance()
        self.case.hosted.policy["lines"][1]["state"] = "inactive"

        # Act
        error = baseline.record_error(self.case.stage)

        # Assert
        self.assertRegex(str(error), "line-inactive")
        self.assertEqual([], self.case.hosted.writes)

    def test_other_line_provenance_cannot_authorize_maintenance(self):
        """The original selected workflow ref remains part of the signed candidate identity."""
        # Arrange
        self.maintenance()
        self.case.statement["predicate"]["buildDefinition"]["externalParameters"]["workflow"]["ref"] = "refs/heads/main"
        self.case.write_bundle("release-provenance.jsonl", self.case.statement)

        # Act
        error = baseline.record_error(self.case.stage)

        # Assert
        self.assertRegex(str(error), "provenance-workflow")
        self.assertEqual([], self.case.hosted.calls)

    def test_historical_signature_verification_does_not_require_current_activation(self):
        """The original signed maintenance ref remains verifiable after its publication grant expires."""
        # Arrange
        self.maintenance()
        self.case.hosted.policy["lines"][1]["state"] = "inactive"

        # Act
        manifest = release.attest_verify(self.case.candidate, self.case.provenance,
                                         commands=self.case.hosted, environment=self.case.environment)

        # Assert
        self.assertEqual("refs/heads/maintenance/1.x", manifest["workflow"]["ref"])
        self.assertTrue(all(call[:3] == ["gh", "attestation", "verify"] for call in self.case.hosted.calls))

    def test_historical_completion_does_not_require_a_retired_source_branch(self):
        """Original signed immutable evidence remains verifiable after the line is retired and removed."""
        # Arrange
        self.maintenance()
        case = self.case
        case.hosted.existing_release(draft=False)
        case.hosted.policy["lines"][1]["state"] = "inactive"
        case.hosted.remote_commit = ""

        # Act
        result = release.complete(case.candidate, case.provenance, case.readback, case.output, **case.options)

        # Assert
        self.assertEqual("complete", result["state"])
        self.assertFalse(any(call[:2] == ["gh", "api"] and "branches/" in call[-1] for call in case.hosted.calls))
        self.assertFalse(any(call[:3] == ["git", "ls-remote", "--refs"] for call in case.hosted.calls))
        self.assertEqual([], case.hosted.writes)

    def test_historical_completion_still_rejects_an_unverified_tag(self):
        """Retiring a line does not bypass independent tag signature verification."""
        # Arrange
        self.maintenance()
        case = self.case
        case.hosted.existing_release(draft=False)
        case.hosted.policy["lines"][1]["state"] = "inactive"
        case.hosted.github_signature = False

        # Act
        error = baseline.record_error(lambda: release.complete(case.candidate, case.provenance, case.readback,
                                                                case.output, **case.options))

        # Assert
        self.assertRegex(str(error), "remote-tag-signature")
        self.assertEqual([], case.hosted.writes)

    def test_prepared_preflight_qualifies_without_publisher_environment(self):
        """Prepared sources retain full own-line validation without requesting publication permissions."""
        # Arrange
        self.maintenance(state="prepared", qualification_only=True)

        # Act
        with patch("eng.release.nuget.check_unused_version", return_value={"packages": []}):
            result = release.preflight(self.case.version, commands=self.case.hosted,
                                       environment=self.case.environment, source_repo=self.case.directory,
                                       qualification_only=True)

        # Assert
        self.assertFalse(result["publishAllowed"])
        self.assertTrue(result["line"]["qualificationOnly"])
        self.assertEqual("net1.0", result["toolchain"]["targetFramework"])
        self.assertFalse(any("environments/nuget" in call[-1] for call in self.case.hosted.calls))
        self.assertEqual([], self.case.hosted.writes)

    def test_prepared_candidate_cannot_be_published(self):
        """Attested qualification-only bytes never acquire write eligibility."""
        # Arrange
        self.maintenance(state="prepared", qualification_only=True)

        # Act
        error = baseline.record_error(self.case.stage)

        # Assert
        self.assertRegex(str(error), "workspace_only")
        self.assertEqual([], self.case.hosted.writes)

    def test_workspace_classification_cannot_borrow_hosted_identity(self):
        """A local schema-one candidate stays ineligible even with matching hosted environment variables."""
        # Arrange
        self.case.manifest["publishable"] = False
        self.seal()

        # Act
        error = baseline.record_error(lambda: release.attest_verify(self.case.candidate, self.case.provenance,
                                                                    commands=self.case.hosted,
                                                                    environment=self.case.environment))

        # Assert
        self.assertRegex(str(error), "workspace_only")
        self.assertEqual([], self.case.hosted.calls)

    def test_authority_revoked_during_tag_check_blocks_first_write(self):
        """The last current-policy check is after the slower cryptographic and tag checks."""
        # Arrange
        def revoke():
            self.case.hosted.policy["lines"][0]["state"] = "inactive"

        self.case.hosted.during_signer_verification = revoke

        # Act
        error = baseline.record_error(self.case.stage)

        # Assert
        self.assertRegex(str(error), "line-inactive")
        self.assertEqual([], self.case.hosted.writes)

    def test_legacy_main_candidate_is_not_reinterpreted_as_maintenance(self):
        """Moving a major's grant cannot relabel a historically main-only signed source."""
        # Arrange
        main = self.case.hosted.policy["lines"][0]
        maintenance = {**main, "role": "maintenance", "sourceRef": "refs/heads/maintenance/1.x"}
        main["packageMajor"] = 2
        self.case.hosted.policy["lines"].append(maintenance)

        # Act
        error = baseline.record_error(self.case.stage)

        # Assert
        self.assertRegex(str(error), "line-version")
        self.assertEqual([], self.case.hosted.writes)

    def test_missing_lock_blocks_before_external_calls(self):
        """Publication cannot use per-version qualification concurrency as its global exclusion."""
        # Arrange
        self.case.environment.pop("NESTEDSET_PUBLICATION_LOCK_HELD")

        # Act
        error = baseline.record_error(self.case.stage)

        # Assert
        self.assertRegex(str(error), "publication-lock")
        self.assertEqual([], self.case.hosted.calls)

    def test_recheck_reuses_exact_authorized_bytes_without_repeating_signature_commands(self):
        """The existing authorization receipt binds portable provenance at each package boundary."""
        # Arrange
        case = self.case
        manifest = release.authorize(case.candidate, case.provenance, commands=case.hosted,
                                     environment=case.environment)
        authorization = case.directory / "authorization.json"
        write_json(authorization, release.receipt(manifest, case.candidate, case.provenance,
                                                 state="authorize", environment=case.environment))
        case.hosted.calls.clear()

        # Act
        actual = release.recheck(case.candidate, case.provenance, authorization,
                                 commands=case.hosted, environment=case.environment)

        # Assert
        self.assertEqual(manifest, actual)
        self.assertFalse(any(call[:3] == ["gh", "attestation", "verify"] for call in case.hosted.calls))
        self.assertEqual([], case.hosted.writes)

    def test_recheck_rejects_changed_provenance_before_other_boundary_reads(self):
        """A previously accepted filename cannot conceal a replaced portable attestation bundle."""
        # Arrange
        case = self.case
        authorization = case.directory / "authorization.json"
        write_json(authorization, release.receipt(case.manifest, case.candidate, case.provenance,
                                                 state="authorize", environment=case.environment))
        case.write_bundle("release-provenance.jsonl", {"tampered": True})

        # Act
        error = baseline.record_error(lambda: release.recheck(case.candidate, case.provenance, authorization,
                                                              commands=case.hosted, environment=case.environment))

        # Assert
        self.assertRegex(str(error), "authorization-changed")
        self.assertEqual([], case.hosted.calls)

    def test_package_boundary_rechecks_current_permission_after_authorization(self):
        """A previously authorized attempt cannot obtain credentials after its grant is revoked."""
        # Arrange
        case = self.case
        authorization = case.directory / "authorization.json"
        write_json(authorization, release.receipt(case.manifest, case.candidate, case.provenance,
                                                 state="authorize", environment=case.environment))
        case.hosted.policy["lines"][0]["state"] = "inactive"

        # Act
        error = baseline.record_error(lambda: release.recheck(case.candidate, case.provenance, authorization,
                                                              commands=case.hosted, environment=case.environment))

        # Assert
        self.assertRegex(str(error), "line-inactive")
        self.assertEqual([], case.hosted.writes)

    def test_prerelease_publication_never_promotes_latest(self):
        """A valid RC is published as prerelease while leaving stable Latest untouched."""
        # Arrange
        self.case.hosted.existing_release()
        self.case.hosted.latest = {"id": 99, "tag_name": "v10.0.0"}

        # Act
        result = self.case.publish()

        # Assert
        self.assertEqual("published", result["state"])
        edit = [call for call in self.case.hosted.writes if call[2] == "edit"][0]
        self.assertIn("--prerelease=true", edit)
        self.assertIn("--latest=false", edit)
        self.assertEqual("v10.0.0", self.case.hosted.latest["tag_name"])

    def test_missing_policy_bootstrap_is_explicit_and_read_only(self):
        """Only the requested bootstrap observation can return actual policy absence."""
        # Arrange
        case = self.case
        case.hosted.api_overrides[f"contents/eng/release/lines-policy.json?ref={case.hosted.authority_commit}"] = None

        # Act
        result = release.observe_authority(case.hosted, allow_missing=True)

        # Assert
        self.assertIsNone(result)
        self.assertEqual([], case.hosted.writes)

    def test_policy_blob_conflict_is_rejected(self):
        """Current policy must equal the authenticated Git blob, not just parse as permissive JSON."""
        # Arrange
        case = self.case
        route = f"contents/eng/release/lines-policy.json?ref={case.hosted.authority_commit}"
        value = json.loads(case.hosted.api(route))
        value["sha"] = "0" * 40
        case.hosted.api_overrides[route] = value

        # Act
        error = baseline.record_error(lambda: release.observe_authority(case.hosted))

        # Assert
        self.assertRegex(str(error), "line-authority")
        self.assertEqual([], case.hosted.writes)

    def test_unprotected_authority_cannot_use_missing_policy_bootstrap(self):
        """An unavailable protected main cannot become a permissive legacy PR base."""
        # Arrange
        self.case.hosted.api_overrides["branches/main"] = {"protected": False, "commit": {"sha": "d" * 40}}

        # Act
        error = baseline.record_error(lambda: release.observe_authority(self.case.hosted, allow_missing=True))

        # Assert
        self.assertRegex(str(error), "line-authority")
        self.assertEqual([], self.case.hosted.writes)

    def test_environment_without_reviewers_blocks_all_writes(self):
        """A line grant does not replace the existing protected publisher approval."""
        # Arrange
        self.case.hosted.api_overrides["environments/nuget"] = {
            "name": "nuget", "protection_rules": [],
            "deployment_branch_policy": {"protected_branches": True, "custom_branch_policies": False}}

        # Act
        error = baseline.record_error(self.case.stage)

        # Assert
        self.assertRegex(str(error), "publisher-environment")
        self.assertEqual([], self.case.hosted.writes)

    def test_exact_custom_deployment_grants_preserve_single_maintainer_approval(self):
        """One reviewer, self-review policy and administrator bypass remain operator-owned choices."""
        # Arrange
        hosted = self.case.hosted
        environment = json.loads(hosted.api("environments/nuget"))
        environment["deployment_branch_policy"] = {"protected_branches": False, "custom_branch_policies": True}
        hosted.api_overrides["environments/nuget"] = environment
        hosted.api_overrides["environments/nuget/deployment-branch-policies?per_page=100&page=1"] = {
            "total_count": 1, "branch_policies": [{"type": "branch", "name": "main"}]}

        # Act
        result = self.case.stage()

        # Assert
        self.assertEqual("staged", result["state"])
        self.assertTrue(environment["can_admins_bypass"])
        self.assertFalse(environment["protection_rules"][0]["prevent_self_review"])
        self.assertEqual(1, len(environment["protection_rules"][0]["reviewers"]))

    def test_wildcard_deployment_grants_cannot_replace_exact_active_sources(self):
        """A broad hosted grant cannot silently activate unknown maintenance branches."""
        # Arrange
        hosted = self.case.hosted
        environment = json.loads(hosted.api("environments/nuget"))
        environment["deployment_branch_policy"] = {"protected_branches": False, "custom_branch_policies": True}
        hosted.api_overrides["environments/nuget"] = environment
        hosted.api_overrides["environments/nuget/deployment-branch-policies?per_page=100&page=1"] = {
            "total_count": 1, "branch_policies": [{"type": "branch", "name": "*"}]}

        # Act
        error = baseline.record_error(self.case.stage)

        # Assert
        self.assertRegex(str(error), "publisher-environment")
        self.assertEqual([], hosted.writes)


if __name__ == "__main__":
    unittest.main()

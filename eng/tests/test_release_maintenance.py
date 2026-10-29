"""Adversarial native-line dispatch, producer, audit and preparation-PR controls."""

import base64
import copy
import hashlib
import itertools
import json
import tempfile
import threading
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

from eng.release import maintenance
from eng.release.common import REPOSITORY, ReleaseError
from eng.tests.test_release_dependencies import policy


ROOT = Path(__file__).resolve().parents[2]
COMMIT = "a" * 40
CORRELATION = "schedule-42-1"


def environment(workflow="ci.yml", ref="refs/heads/main"):
    """Expose only exact authenticated coordinator/child fields consumed by the guard."""
    return {"GITHUB_EVENT_NAME": "workflow_dispatch", "GITHUB_REPOSITORY": REPOSITORY,
            "GITHUB_REF": ref, "GITHUB_SHA": COMMIT,
            "GITHUB_WORKFLOW_REF": f"{REPOSITORY}/.github/workflows/{workflow}@{ref}",
            "GITHUB_RUN_ID": "42", "GITHUB_RUN_ATTEMPT": "1",
            "INPUT_MAINTENANCE": "true", "INPUT_CORRELATION": CORRELATION, "INPUT_SOURCE_SHA": COMMIT}


class NativeDispatchFixture:
    """Emulate exact GitHub observations without network writes, delays or a second client implementation."""

    def __init__(self):
        self.dispatches = []
        self.runs = {}
        self.changed_run = {}
        self.job_mutation = lambda jobs: jobs
        self.protected = True
        self.return_run_id = True
        self.lock = threading.Lock()
        self.source_mutation = lambda source: source

    def request(self, commands, route, **options):
        """Return the authenticated branch, source, run or actual producer inventory requested."""
        if route.startswith("branches/"):
            return {"name": route.removeprefix("branches/").replace("%2F", "/"),
                    "protected": self.protected, "commit": {"sha": COMMIT}}

        if route.startswith("contents/"):
            path = route.removeprefix("contents/").split("?ref=", 1)[0]
            text = self.source_mutation((ROOT / path).read_text())
            raw = text.encode("ascii")

            return {"type": "file", "path": path, "encoding": "base64",
                    "content": base64.b64encode(raw).decode(),
                    "sha": hashlib.sha1(b"blob " + str(len(raw)).encode() + b"\0" + raw).hexdigest()}

        if route.startswith("actions/runs/") and "/jobs?" in route:
            run_id = int(route.split("/")[2])
            workflow = self.runs[run_id]["path"].split("/")[-1]
            expected = (maintenance.qualification_producers((ROOT / ".github/workflows/ci.yml").read_text())
                        if workflow == "ci.yml" else ["schedule-identity", "dependency-vulnerability-audit"])
            jobs = self.job_mutation([{"id": index + 1, "name": name, "status": "completed", "conclusion": "success"}
                                      for index, name in enumerate(expected)])

            return {"total_count": len(jobs), "jobs": jobs}

        if route.startswith("actions/runs/"):
            return {**self.runs[int(route.split("/")[2])], **self.changed_run}

        raise AssertionError(route)

    def dispatch(self, commands, route, method, payload=None):
        """Bind the returned ID to the actual requested workflow/ref/correlation."""
        with self.lock:
            run_id = 101 + len(self.dispatches)
            self.dispatches.append((route, method, copy.deepcopy(payload)))
            self.runs[run_id] = {"id": run_id, "event": "workflow_dispatch", "head_sha": COMMIT,
                                 "head_branch": payload["ref"], "repository": {"full_name": REPOSITORY},
                                 "path": ".github/workflows/" + route.split("/")[2],
                                 "display_title": "maintenance " + payload["inputs"]["schedule-correlation"],
                                 "status": "completed", "conclusion": "success", "run_attempt": 1}

        return {"workflow_run_id": run_id} if self.return_run_id else {}


class MaintenanceTests(unittest.TestCase):
    """Dispatch evidence must correspond to exact active branches and complete successful work."""

    def setUp(self):
        """Use the existing injectable API helpers and an isolated local diagnostic report."""
        self.policy = policy()
        self.fixture = NativeDispatchFixture()
        self.commands = object()
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.output = Path(self.temporary.name) / "maintenance.json"
        self.api = patch.object(maintenance.publication, "api", side_effect=self.fixture.request)
        self.post = patch.object(maintenance.publication, "api_request", side_effect=self.fixture.dispatch, create=True)
        self.api.start()
        self.post.start()
        self.addCleanup(self.api.stop)
        self.addCleanup(self.post.stop)

    def test_active_lines_each_use_own_ci_once(self):
        """Default and maintenance sources each receive their actual full parallel CI."""
        # Arrange
        authority = {"commit": COMMIT, "policy": self.policy}
        current = environment(maintenance.WORKFLOW)

        # Act
        report = maintenance.coordinate_lines(self.commands, authority, "qualification", current, self.output)

        # Assert
        self.assertEqual("success", report["conclusion"])
        self.assertEqual({"main", "maintenance/10.x"}, {call[2]["ref"] for call in self.fixture.dispatches})
        self.assertEqual(2, len(self.fixture.dispatches))
        self.assertTrue(all(call[0] == "actions/workflows/ci.yml/dispatches" for call in self.fixture.dispatches))
        self.assertTrue(all(call[2]["inputs"]["schedule-source-sha"] == COMMIT for call in self.fixture.dispatches))
        self.assertEqual(report, json.loads(self.output.read_text()))

    def test_failed_native_producer_does_not_cancel_sibling_dispatch(self):
        """An apparently successful workflow wrapper cannot replace a skipped concrete producer."""
        # Arrange
        authority = {"commit": COMMIT, "policy": self.policy}
        self.fixture.job_mutation = lambda jobs: [*jobs[:-1], {**jobs[-1], "conclusion": "skipped"}]

        # Act
        report = maintenance.coordinate_lines(self.commands, authority, "qualification",
                                              environment(maintenance.WORKFLOW), self.output)

        # Assert
        self.assertEqual("failure", report["conclusion"])
        self.assertEqual(2, len(self.fixture.dispatches))
        self.assertTrue(all(item["verificationStatus"] == "failed" for item in report["children"]))
        self.assertTrue(all("reason" in item for item in report["children"]))

    def test_prepared_and_inactive_sources_are_not_dispatched(self):
        """Only currently active lines acquire scheduled work."""
        # Arrange
        self.policy["lines"][0]["state"] = "prepared"
        self.policy["lines"][1]["state"] = "inactive"

        # Act
        report = maintenance.coordinate_lines(self.commands, {"commit": COMMIT, "policy": self.policy}, "audit",
                                              environment(maintenance.WORKFLOW), self.output)

        # Assert
        self.assertEqual("success", report["conclusion"])
        self.assertEqual([], report["children"])
        self.assertEqual([], self.fixture.dispatches)

    def test_wrong_coordinator_source_is_rejected_before_dispatch(self):
        """A maintenance branch cannot coordinate grants in place of current protected main."""
        # Arrange
        current = environment(maintenance.WORKFLOW, "refs/heads/maintenance/10.x")

        # Act
        with self.assertRaises(ReleaseError):
            maintenance.coordinate_lines(self.commands, {"commit": COMMIT, "policy": self.policy},
                                         "audit", current, self.output)

        # Assert
        self.assertEqual([], self.fixture.dispatches)

    def test_current_ci_expands_every_test_and_sample_cell(self):
        """The real workflow is the producer inventory rather than a copied matrix count."""
        # Arrange
        source = (ROOT / ".github/workflows/ci.yml").read_text()

        # Act
        names = maintenance.qualification_producers(source)

        # Assert
        self.assertEqual(22, len(names))
        self.assertIn("MySQL integration tests", names)
        self.assertIn("MariaDB capacity tests", names)
        self.assertIn("SafeMigrations integration tests", names)
        self.assertIn("SQLite sample / SignalLab", names)
        self.assertIn("schedule-identity", names)

    def test_matrix_extra_fields_cannot_hide_a_producer(self):
        """Unknown, repeated, incomplete and expression-based row shapes fail closed."""
        # Arrange
        source = (ROOT / ".github/workflows/ci.yml").read_text()
        mutations = (source.replace("            docker: false", "            mystery: false", 1),
                     source.replace("            docker: false", "            docker: false\n            docker: true", 1),
                     source.replace("          - id: core", "          - strange: core", 1),
                     source.replace("sample: [FileSystem, SignalLab, UserGroups]", "sample: ${{ inputs.samples }}"),
                     source.replace("\n            filter: FullyQualifiedName~.MariaDb.&Lane!=Allocation", "", 1),
                     source.replace("filter: Category!=Allocation", "filter: &unit Category!=Allocation", 1))

        # Act
        errors = []
        for changed in mutations:
            try:
                maintenance.qualification_producers(changed)
            except ReleaseError as error:
                errors.append(error.code)

        # Assert
        self.assertEqual(["maintenance-matrix"] * len(mutations), errors)

    def test_matrix_mapping_key_order_does_not_change_producers(self):
        """Every key order of a literal include row identifies the same actual test producer."""
        # Arrange
        source = (ROOT / ".github/workflows/ci.yml").read_text()
        original = ("          - id: core\n            name: Core unit tests\n"
                    "            project: tests/Doka.NestedSet.Tests/Doka.NestedSet.Tests.csproj\n"
                    "            docker: false")
        fields = ("id: core", "name: Core unit tests",
                  "project: tests/Doka.NestedSet.Tests/Doka.NestedSet.Tests.csproj", "docker: false")
        expected = maintenance.qualification_producers(source)

        # Act
        observed = []
        for order in itertools.permutations(fields):
            replacement = "          - " + order[0] + "\n" + "\n".join("            " + value for value in order[1:])
            observed.append(maintenance.qualification_producers(source.replace(original, replacement)))

        # Assert
        self.assertEqual([expected] * 24, observed)

    def test_missing_duplicate_and_skipped_native_cells_are_rejected(self):
        """A green final wrapper does not excuse a missing concrete database producer."""
        # Arrange
        variants = (lambda jobs: [job for job in jobs if job["name"] != "MariaDB capacity tests"],
                    lambda jobs: [*jobs, {**next(job for job in jobs if job["name"] == "SQLite integration tests"),
                                         "id": 900}],
                    lambda jobs: [{**job, "conclusion": "skipped"} if job["name"] == "SafeMigrations integration tests"
                                  else job for job in jobs])
        errors = []

        # Act
        for changed in variants:
            self.fixture.job_mutation = changed
            try:
                maintenance.dispatch_line(self.commands, "ci.yml", "refs/heads/main", CORRELATION)
            except ReleaseError as error:
                errors.append(error.code)

        # Assert
        self.assertEqual(["maintenance-producer"] * len(variants), errors)

    def test_workflow_blob_tampering_is_rejected_before_dispatch(self):
        """Authenticated metadata alone does not bless changed workflow bytes."""
        # Arrange
        original = self.fixture.request

        def tamper(commands, route, **options):
            result = original(commands, route, **options)
            if route.startswith("contents/"):
                result["sha"] = "b" * 40

            return result

        with patch.object(maintenance.publication, "api", side_effect=tamper):
            # Act
            with self.assertRaises(ReleaseError) as caught:
                maintenance.dispatch_line(self.commands, maintenance.WORKFLOW, "refs/heads/main", CORRELATION)

        # Assert
        self.assertEqual("maintenance-source", caught.exception.code)
        self.assertEqual([], self.fixture.dispatches)

    def test_unprotected_branch_is_rejected_before_dispatch(self):
        """Policy membership cannot replace actual source protection."""
        # Arrange
        self.fixture.protected = False

        # Act
        with self.assertRaises(ReleaseError):
            maintenance.dispatch_line(self.commands, maintenance.WORKFLOW, "refs/heads/main", CORRELATION)

        # Assert
        self.assertEqual([], self.fixture.dispatches)

    def test_empty_dispatch_response_is_not_replaced_by_a_run_search(self):
        """Only the actual returned run ID can identify the child under overlapping dispatches."""
        # Arrange
        self.fixture.return_run_id = False

        # Act
        with self.assertRaises(ReleaseError) as caught:
            maintenance.dispatch_line(self.commands, maintenance.WORKFLOW, "refs/heads/main", CORRELATION)

        # Assert
        self.assertEqual("maintenance-identity", caught.exception.code)
        self.assertEqual(1, len(self.fixture.dispatches))

    def test_wrong_child_identity_is_rejected(self):
        """Foreign SHA/ref/workflow/correlation cannot satisfy an audit observation."""
        # Arrange
        run = self.fixture.dispatch(self.commands, f"actions/workflows/{maintenance.WORKFLOW}/dispatches", "POST",
                                    {"ref": "main", "inputs": {"schedule-correlation": CORRELATION}})["workflow_run_id"]
        variants = ({"head_sha": "b" * 40}, {"head_branch": "maintenance/10.x"}, {"display_title": "other"},
                    {"path": ".github/workflows/release-candidate.yml"}, {"repository": {"full_name": "other/repo"}})

        # Act
        errors = []
        for changed in variants:
            self.fixture.changed_run = changed
            try:
                maintenance.wait_for_child(self.commands, run, maintenance.WORKFLOW, "refs/heads/main",
                                           COMMIT, CORRELATION, ["schedule-identity"])
            except ReleaseError as error:
                errors.append(error.code)

        # Assert
        self.assertEqual(["maintenance-child"] * len(variants), errors)

    def test_timeout_retains_no_success_claim(self):
        """A queued child does not become successful merely because dispatch succeeded."""
        # Arrange
        run = self.fixture.dispatch(self.commands, f"actions/workflows/{maintenance.WORKFLOW}/dispatches", "POST",
                                    {"ref": "main", "inputs": {"schedule-correlation": CORRELATION}})["workflow_run_id"]
        self.fixture.changed_run = {"status": "queued", "conclusion": None}
        times = iter((0, 2))

        # Act
        with self.assertRaises(ReleaseError) as caught:
            maintenance.wait_for_child(self.commands, run, maintenance.WORKFLOW, "refs/heads/main", COMMIT,
                                       CORRELATION, [], timeout=1, clock=lambda: next(times), sleep=lambda _: None)

        # Assert
        self.assertEqual("maintenance-timeout", caught.exception.code)

    def test_child_guard_preserves_ordinary_feature_ci(self):
        """An ordinary manual feature run needs no active publication line."""
        # Arrange
        current = environment(ref="refs/heads/feature/change")
        current["INPUT_MAINTENANCE"] = "false"

        # Act
        guarded = maintenance.validate_child(None, "ci.yml", current)

        # Assert
        self.assertFalse(guarded)

    def test_child_guard_rejects_changed_source_or_inactive_line(self):
        """Explicit maintenance remains bound to current active source and exact dispatch SHA."""
        # Arrange
        current = environment()
        self.policy["lines"][0]["state"] = "inactive"

        # Act
        with self.assertRaises(ReleaseError):
            maintenance.validate_child(self.policy, "ci.yml", current)

        # Assert
        self.assertEqual(COMMIT, current["GITHUB_SHA"])

    def test_complete_job_pages_require_a_terminal_page(self):
        """A declared hundred jobs cannot hide contradictory jobs on the next page."""
        # Arrange
        jobs = [{"id": index, "name": "required" if index == 1 else f"other-{index}",
                 "status": "completed", "conclusion": "success"} for index in range(1, 101)]
        pages = iter(({"total_count": 100, "jobs": jobs}, {"total_count": 100, "jobs": []}))
        with patch.object(maintenance.publication, "api", side_effect=lambda *args, **kwargs: next(pages)) as reader:
            # Act
            maintenance.verify_jobs(self.commands, 101, 1, ["required"])

        # Assert
        self.assertEqual(2, reader.call_count)
        self.assertIn("/attempts/1/jobs", reader.call_args_list[0].args[1])

    def test_incomplete_changing_or_repeated_job_pages_fail(self):
        """Incomplete totals and repeated job identities remain failures even with a successful required job."""
        # Arrange
        job = {"id": 1, "name": "required", "status": "completed", "conclusion": "success"}
        variants = ({"total_count": 2, "jobs": [job]}, {"total_count": 2, "jobs": [job, job]},
                    {"total_count": 0, "jobs": []}, {"total_count": True, "jobs": [job]})
        failures = []

        # Act
        for page in variants:
            with patch.object(maintenance.publication, "api", return_value=page):
                try:
                    maintenance.verify_jobs(self.commands, 101, 1, ["required"])
                except ReleaseError as error:
                    failures.append(error.code)

        # Assert
        self.assertEqual(["maintenance-jobs"] * len(variants), failures)


class AuditTests(unittest.TestCase):
    """A clean result must cover the entire actual solution after fresh advisory restore."""

    def setUp(self):
        """Use two real-shaped solution paths and the SDK's clean finding-only report."""
        self.projects = [str(ROOT / "src/Doka.NestedSet/Doka.NestedSet.csproj"),
                         str(ROOT / "samples/SignalLab/SignalLab.csproj")]
        self.clean = {"version": 1, "parameters": "--vulnerable --include-transitive --format json",
                      "sources": ["https://api.nuget.org/v3/index.json"],
                      "projects": [{"path": path} for path in self.projects]}

    def test_clean_report_may_omit_finding_only_frameworks(self):
        """SDK clean scans do not need fabricated empty package/framework entries."""
        # Arrange
        report = copy.deepcopy(self.clean)

        # Act
        result = maintenance.validate_audit(report, self.projects)

        # Assert
        self.assertEqual({"schemaVersion": 1, "conclusion": "success", "projectCount": 2}, result)

    def test_direct_and_transitive_advisories_reject_exit_zero_report(self):
        """Both package kinds remain security findings even when the SDK process succeeded."""
        # Arrange
        reports = []
        for kind in ("topLevelPackages", "transitivePackages"):
            report = copy.deepcopy(self.clean)
            report["projects"][0]["frameworks"] = [{"framework": "net10.0", kind: [
                {"id": "Some.Dependency", "resolvedVersion": "1.0.0", "vulnerabilities": [{"severity": "High"}]}]}]
            reports.append(report)

        # Act
        failures = []
        for report in reports:
            try:
                maintenance.validate_audit(report, self.projects)
            except ReleaseError as error:
                failures.append(error.code)

        # Assert
        self.assertEqual(["maintenance-audit", "maintenance-audit"], failures)

    def test_missing_duplicate_project_and_advisory_errors_are_rejected(self):
        """Partial projects, suppressed advisory failures and changed sources never imply clean evidence."""
        # Arrange
        missing = copy.deepcopy(self.clean)
        missing["projects"].pop()
        duplicate = copy.deepcopy(self.clean)
        duplicate["projects"] = [duplicate["projects"][0]] * 2
        advisory = {**self.clean, "warnings": ["Unable to retrieve advisory data"]}
        source = {**self.clean, "sources": ["https://private.invalid/index.json"]}

        # Act
        failures = []
        for report in (missing, duplicate, advisory, source):
            try:
                maintenance.validate_audit(report, self.projects)
            except ReleaseError as error:
                failures.append(error.code)

        # Assert
        self.assertEqual(["maintenance-audit"] * 4, failures)

    def test_actual_audit_commands_refresh_and_validate_real_solution_inventory(self):
        """The real audit orchestration uses audited restore and parses a fresh complete SDK report."""
        # Arrange
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "audit"
            calls = []

            def execute(command, **options):
                calls.append((command, options))
                if command[1:3] == ["package", "list"]:
                    options["stdout"].write(json.dumps(self.clean))

                return SimpleNamespace(returncode=0)

            with patch.object(maintenance, "solution_projects", return_value=[Path(path) for path in self.projects]), \
                    patch.object(maintenance.subprocess, "run", side_effect=execute):
                # Act
                result = maintenance.run_audit(ROOT, output)

            # Assert
            self.assertEqual(2, result["projectCount"])
            self.assertIn("--locked-mode", calls[0][0])
            self.assertIn("--no-http-cache", calls[0][0])
            self.assertIn("-p:NuGetAuditMode=all", calls[0][0])
            self.assertIn("-p:NoWarn=", calls[0][0])
            self.assertIn("--include-transitive", calls[1][0])
            self.assertEqual(str((output / "http-cache").resolve()), calls[0][1]["env"]["NUGET_HTTP_CACHE_PATH"])
            self.assertEqual(result, json.loads((output / "summary.json").read_text()))

    def test_restore_failure_finalizes_failure_summary_and_never_lists_packages(self):
        """A failing fresh restore cannot be replaced by stale successful audit output."""
        # Arrange
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "audit"
            with patch.object(maintenance, "solution_projects", return_value=[Path(path) for path in self.projects]), \
                    patch.object(maintenance.subprocess, "run", return_value=SimpleNamespace(returncode=1)) as run:
                # Act
                with self.assertRaises(ReleaseError):
                    maintenance.run_audit(ROOT, output)

            # Assert
            self.assertEqual(1, run.call_count)
            self.assertEqual({"schemaVersion": 1, "conclusion": "failure", "stage": "restore",
                              "errorCode": "maintenance-audit"}, json.loads((output / "summary.json").read_text()))
            self.assertFalse((output / "audit.json").exists())

    def test_advisory_failure_finalizes_summary_without_disclosing_raw_errors(self):
        """A failing advisory command retains its log and a sanitized completed failure state."""
        # Arrange
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "audit"

            def execute(command, **options):
                if command[1:3] == ["package", "list"]:
                    options["stderr"].write("private diagnostic value")

                    return SimpleNamespace(returncode=1)

                return SimpleNamespace(returncode=0)

            with patch.object(maintenance, "solution_projects", return_value=[Path(path) for path in self.projects]), \
                    patch.object(maintenance.subprocess, "run", side_effect=execute):
                # Act
                with self.assertRaises(ReleaseError) as caught:
                    maintenance.run_audit(ROOT, output)

            # Assert
            self.assertEqual("maintenance-audit", caught.exception.code)
            self.assertEqual({"schemaVersion": 1, "conclusion": "failure", "stage": "advisories",
                              "errorCode": "maintenance-audit"}, json.loads((output / "summary.json").read_text()))
            self.assertEqual("private diagnostic value", (output / "audit.log").read_text())
            self.assertNotIn("private diagnostic value", (output / "summary.json").read_text())

    def test_malformed_report_finalizes_validation_failure_and_preserves_original_error(self):
        """An exit-zero malformed JSON report is a completed validation failure, not an ongoing audit."""
        # Arrange
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "audit"

            def execute(command, **options):
                if command[1:3] == ["package", "list"]:
                    options["stdout"].write("not-json")

                return SimpleNamespace(returncode=0)

            with patch.object(maintenance, "solution_projects", return_value=[Path(path) for path in self.projects]), \
                    patch.object(maintenance.subprocess, "run", side_effect=execute):
                # Act
                with self.assertRaises(ReleaseError) as caught:
                    maintenance.run_audit(ROOT, output)

            # Assert
            self.assertEqual("invalid_json", caught.exception.code)
            self.assertEqual({"schemaVersion": 1, "conclusion": "failure", "stage": "validation",
                              "errorCode": "invalid_json"}, json.loads((output / "summary.json").read_text()))
            self.assertEqual("not-json", (output / "audit.json").read_text())

    def test_process_failure_records_its_stage_and_reraises_original_exception(self):
        """Process-start failures remain the original error and finalize an existing audit marker."""
        # Arrange
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "audit"
            failure = OSError("private process detail")
            with patch.object(maintenance, "solution_projects", return_value=[Path(path) for path in self.projects]), \
                    patch.object(maintenance.subprocess, "run", side_effect=failure):
                # Act
                with self.assertRaises(OSError) as caught:
                    maintenance.run_audit(ROOT, output)

            # Assert
            self.assertIs(failure, caught.exception)
            self.assertEqual({"schemaVersion": 1, "conclusion": "failure", "stage": "restore",
                              "errorCode": "command-failed"}, json.loads((output / "summary.json").read_text()))
            self.assertNotIn("private process detail", (output / "summary.json").read_text())

    def test_non_object_json_finalizes_named_validation_failure(self):
        """Well-formed JSON with the wrong root shape is rejected without leaking a programming exception."""
        # Arrange
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "audit"

            def execute(command, **options):
                if command[1:3] == ["package", "list"]:
                    options["stdout"].write("[]")

                return SimpleNamespace(returncode=0)

            with patch.object(maintenance, "solution_projects", return_value=[Path(path) for path in self.projects]), \
                    patch.object(maintenance.subprocess, "run", side_effect=execute):
                # Act
                with self.assertRaises(ReleaseError) as caught:
                    maintenance.run_audit(ROOT, output)

            # Assert
            self.assertEqual("maintenance-audit", caught.exception.code)
            self.assertEqual({"schemaVersion": 1, "conclusion": "failure", "stage": "validation",
                              "errorCode": "maintenance-audit"}, json.loads((output / "summary.json").read_text()))

    def test_nonempty_output_is_not_reused_as_fresh_evidence(self):
        """An old audit must not satisfy the next scheduled servicing run."""
        # Arrange
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            (output / "audit.json").write_text(json.dumps(self.clean))
            with patch.object(maintenance, "solution_projects", return_value=[Path(path) for path in self.projects]), \
                    patch.object(maintenance.subprocess, "run") as run:
                # Act
                with self.assertRaises(ReleaseError):
                    maintenance.run_audit(ROOT, output)

            # Assert
            run.assert_not_called()


class PreparationPrTests(unittest.TestCase):
    """Exact PR comparisons support registered inactive lines without granting publication."""

    def setUp(self):
        """Use a fork head while retaining this repository's authenticated base."""
        self.policy = policy()
        self.policy["lines"][1]["state"] = "inactive"
        self.event = {"pull_request": {"base": {"repo": {"full_name": REPOSITORY},
                                               "ref": "maintenance/10.x", "sha": "a" * 40},
                                       "head": {"repo": {"full_name": "contributor/fork"}, "sha": "b" * 40}}}

    def test_registered_inactive_base_accepts_exact_fork_comparison(self):
        """Preparation PRs retain both original revisions before publication activation."""
        # Arrange
        with patch.object(maintenance.publication, "observe_authority",
                          return_value={"policy": self.policy}, create=True):
            # Act
            result = maintenance.check_pr(object(), self.event)

        # Assert
        self.assertEqual({"baseRef": "a" * 40, "headRef": "b" * 40}, result)

    def test_authenticated_missing_policy_bridge_only_accepts_main(self):
        """The first feature PR can install policy, while unknown maintenance bases stay denied."""
        # Arrange
        self.event["pull_request"]["base"]["ref"] = "main"
        with patch.object(maintenance.publication, "observe_authority", return_value=None, create=True):
            # Act
            result = maintenance.check_pr(object(), self.event)

        # Assert
        self.assertEqual("a" * 40, result["baseRef"])

    def test_missing_policy_does_not_admit_maintenance(self):
        """Only an actual authenticated absence gets the main-only bridge; transport errors propagate."""
        # Arrange
        authority = patch.object(maintenance.publication, "observe_authority", return_value=None, create=True)
        with authority:
            # Act
            with self.assertRaises(ReleaseError):
                maintenance.check_pr(object(), self.event)

        # Assert
        self.assertEqual("maintenance/10.x", self.event["pull_request"]["base"]["ref"])

    def test_unavailable_authority_is_not_treated_as_missing(self):
        """Transport/permission failures propagate instead of using the first-install bridge."""
        # Arrange
        self.event["pull_request"]["base"]["ref"] = "main"
        with patch.object(maintenance.publication, "observe_authority",
                          side_effect=ReleaseError("command-failed", "Unavailable authority"), create=True):
            # Act
            with self.assertRaises(ReleaseError) as caught:
                maintenance.check_pr(object(), self.event)

        # Assert
        self.assertEqual("command-failed", caught.exception.code)

    def test_unregistered_base_is_rejected(self):
        """A legitimate fork head cannot authorize an unregistered target branch."""
        # Arrange
        self.event["pull_request"]["base"]["ref"] = "feature/unregistered"
        with patch.object(maintenance.publication, "observe_authority", return_value={"policy": self.policy}, create=True):
            # Act
            with self.assertRaises(ReleaseError):
                maintenance.check_pr(object(), self.event)

        # Assert
        self.assertEqual("b" * 40, self.event["pull_request"]["head"]["sha"])


if __name__ == "__main__":
    unittest.main()

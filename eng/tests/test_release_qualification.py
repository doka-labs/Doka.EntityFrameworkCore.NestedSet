"""Offline qualification regressions for fresh runs, evidence, and process ownership."""

import copy
import io
import json
import os
import signal
import subprocess
import sys
import tempfile
import time
import unittest
import xml.etree.ElementTree as ET
import zipfile
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path
from unittest.mock import Mock, patch

from eng.release import common, qualification, lines
from eng.tests.test_release_lines import policy as line_policy, source as line_source

VERSION = "10.1.0-rc.1"
COMMIT = "a" * 40
TREE = "b" * 40


def capture(action):
    """Keep one tested operation separate from assertions about its failure."""
    try:
        action()
    except common.ReleaseError as error:
        return error

    return None


def text_file(path, content):
    """Create isolated textual evidence without touching the product checkout."""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="ascii")


def stage_fixture(runner):
    """Populate small offline outputs; these bytes never represent qualified .NET packages."""
    for stage in common.STAGES:
        text_file(runner.output / stage / "fixture.txt", f"{stage} evidence")
        text_file(runner.output / "logs" / stage / "fixture.log", f"{stage} passed")

    (runner.output / "packages/fixture.txt").unlink()
    records = []
    for package in common.PACKAGES:
        primary = f"{package}.{runner.version}.nupkg"
        symbols = f"{package}.{runner.version}.snupkg"
        text_file(runner.output / "packages" / primary, f"{package} primary fixture")
        text_file(runner.output / "packages" / symbols, f"{package} symbol fixture")
        records.append({"id": package, "version": runner.version, "nupkg": primary, "snupkg": symbols,
                        "nupkgSha256": common.sha256(runner.output / "packages" / primary),
                        "snupkgSha256": common.sha256(runner.output / "packages" / symbols), "symbols": []})
        common.write_json(runner.output / "sbom" / package / "_manifest/spdx_2.2/manifest.spdx.json",
                          {"spdxVersion": "SPDX-2.2", "name": package})

    common.write_json(runner.output / "packages/package-manifest.json", {"schemaVersion": 1, "packages": records})


class QualificationFixture(unittest.TestCase):
    """Isolate source and hosted identity while forbidding actual Git or database commands."""

    def setUp(self):
        """Build only temporary source/evidence files and mock the Git-derived identity."""
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.repo = self.root / "repo"
        self.repo.mkdir()
        text_file(self.repo / "CHANGELOG.md", "# Changelog\n\n## 10.1.0-rc.1 (2026-09-19)\n\nReviewed change.\n"
                  "\n## 10.1.0-rc.2 (2026-09-19)\n\nNext reviewed change.\n")
        line_source(self.repo)
        self.source = {"commit": COMMIT, "tree": TREE, "fingerprint": "c" * 64,
                       "files": {"CHANGELOG.md": common.sha256(self.repo / "CHANGELOG.md")}, "dirty": False}
        self.source_patch = patch.object(qualification, "source_identity", side_effect=lambda *_: copy.deepcopy(self.source))
        self.source_patch.start()
        self.addCleanup(self.source_patch.stop)
        self.environment = {"GITHUB_ACTIONS": "true", "GITHUB_EVENT_NAME": "workflow_dispatch",
                            "GITHUB_WORKFLOW_REF": f"{common.REPOSITORY}/{common.RELEASE_WORKFLOW}@refs/heads/main",
                            "GITHUB_REPOSITORY": common.REPOSITORY, "GITHUB_SHA": COMMIT,
                            "GITHUB_RUN_ID": "120", "GITHUB_RUN_ATTEMPT": "1", "GITHUB_REF": "refs/heads/main"}
        self.environment["NESTEDSET_RELEASE_LINE"] = json.dumps(lines.freeze_line(
            line_policy(), "refs/heads/main", VERSION, policy_commit=COMMIT))
        environment_patch = patch.dict(os.environ, self.environment, clear=True)
        environment_patch.start()
        self.addCleanup(environment_patch.stop)
        self.runner = qualification.Qualification(self.repo, self.root / "output", VERSION)

    def complete_fixture(self):
        """Seal a complete tiny candidate using the real assembly implementation."""
        stage_fixture(self.runner)
        with redirect_stdout(io.StringIO()):
            self.runner.assemble()

        return self.runner.output / "candidate"


class BuildVersionTests(QualificationFixture):
    """The selected release identity must reach both NuGet archives and assembly metadata."""

    def test_build_receives_selected_assembly_and_package_version(self):
        """A different prerelease version must not inherit the source VersionPrefix in the assembly."""
        # Arrange
        self.runner.version = "0.2.0-rc.1"

        # Act
        properties = self.runner.properties()

        # Assert
        self.assertIn("-p:Version=0.2.0-rc.1", properties)
        self.assertIn("-p:PackageVersion=0.2.0-rc.1", properties)

    def test_build_restores_compiles_packs_and_rechecks_inputs_in_order(self):
        """The shared build produces one inspected package set from unchanged source and locks."""
        # Arrange
        calls = []
        patches = [patch.object(self.runner, name, side_effect=lambda name=name: calls.append(name))
                   for name in ("restore", "compile", "packages", "assert_inputs")]
        for item in patches:
            item.start()
            self.addCleanup(item.stop)

        # Act
        self.runner.build()

        # Assert
        self.assertEqual(["restore", "compile", "packages", "assert_inputs"], calls)


class ReleaseSourceContractTests(QualificationFixture):
    """The hosted RC version must be reviewed in source before build overrides are applied."""

    def test_matching_source_version_and_dated_notes_pass(self):
        """A reviewed source release line produces the exact notes stored in the candidate."""
        # Act
        notes = common.reviewed_release_notes(self.repo, VERSION)

        # Assert
        self.assertEqual("# 10.1.0-rc.1\n\nReviewed change.\n", notes)

    def test_mismatched_source_version_fails(self):
        """An MSBuild Version override cannot silently publish from another source release line."""
        # Arrange
        text_file(self.repo / "src/Directory.Build.props",
                  "<Project><PropertyGroup><VersionPrefix>0.2.0</VersionPrefix></PropertyGroup></Project>\n")

        # Act
        error = capture(lambda: common.reviewed_release_notes(self.repo, VERSION))

        # Assert
        self.assertEqual("source-version", error.code)

    def test_duplicate_source_version_fails(self):
        """A second MSBuild version value cannot make release-line selection ambiguous."""
        # Arrange
        text_file(self.repo / "src/Directory.Build.props", "<Project><PropertyGroup><VersionPrefix>10.1.0</VersionPrefix>"
                  "<VersionPrefix>10.1.0</VersionPrefix></PropertyGroup></Project>\n")

        # Act
        error = capture(lambda: common.reviewed_release_notes(self.repo, VERSION))

        # Assert
        self.assertEqual("source-version", error.code)

    def test_duplicate_changelog_section_fails(self):
        """An ambiguous second section cannot choose release notes by first-match order."""
        # Arrange
        with (self.repo / "CHANGELOG.md").open("a", encoding="ascii") as stream:
            stream.write("\n## 10.1.0-rc.1 (2026-09-20)\n\nDifferent notes.\n")

        # Act
        error = capture(lambda: common.reviewed_release_notes(self.repo, VERSION))

        # Assert
        self.assertEqual("release-notes", error.code)

    def test_undated_changelog_section_fails(self):
        """An unreleased or undated heading cannot authorize a hosted candidate."""
        # Arrange
        path = self.repo / "CHANGELOG.md"
        path.write_text(path.read_text(encoding="ascii").replace("(2026-09-19)", "(unreleased)", 1), encoding="ascii")

        # Act
        error = capture(lambda: common.reviewed_release_notes(self.repo, VERSION))

        # Assert
        self.assertEqual("release-notes", error.code)

    def test_invalid_calendar_date_fails(self):
        """A date-shaped but impossible release date is not a reviewed dated entry."""
        # Arrange
        path = self.repo / "CHANGELOG.md"
        path.write_text(path.read_text(encoding="ascii").replace("2026-09-19", "2026-02-30", 1), encoding="ascii")

        # Act
        error = capture(lambda: common.reviewed_release_notes(self.repo, VERSION))

        # Assert
        self.assertEqual("release-notes", error.code)

    def test_empty_release_notes_fail(self):
        """A dated heading alone cannot create a usable GitHub release body."""
        # Arrange
        text_file(self.repo / "CHANGELOG.md", "# Changelog\n\n## 10.1.0-rc.1 (2026-09-19)\n\n"
                  "## 10.1.0-rc.2 (2026-09-19)\n\nNext reviewed change.\n")

        # Act
        error = capture(lambda: common.reviewed_release_notes(self.repo, VERSION))

        # Assert
        self.assertEqual("release-notes", error.code)


class BuildPrerequisiteTests(QualificationFixture):
    """Keep decision documents and containers out of the package build."""

    def build_commands(self):
        """Record the build's process names without running .NET or inspecting real archives."""
        with patch.object(self.runner, "command") as command, patch.object(qualification, "prepare_locks"), \
                patch.object(qualification, "verify_locks"), patch.object(self.runner, "packages"):
            self.runner.build()

        return command.call_args_list

    def test_build_does_not_gate_on_decision_documents(self):
        """Invalid ADR content must not make package qualification fail."""
        # Act
        commands = self.build_commands()

        # Assert
        self.assertNotIn("adrs", [call.args[1] for call in commands])
        self.assertNotIn("validate-adrs.sh", str(commands))

    def test_build_does_not_require_docker(self):
        """Package production must remain usable without a running daemon."""
        # Act
        commands = self.build_commands()

        # Assert
        self.assertEqual(["restore", "build"], [call.args[1] for call in commands])


class RunIdentityTests(QualificationFixture):
    """Each qualification creates fresh evidence for one source and version."""

    def test_existing_output_is_rejected(self):
        """A retry cannot silently reuse artifacts from an earlier attempt."""
        # Arrange
        original = (self.runner.output / "identity.json").read_bytes()

        # Act
        error = capture(lambda: qualification.Qualification(self.repo, self.runner.output, VERSION))

        # Assert
        self.assertIsInstance(error, common.ReleaseError)
        self.assertEqual("output_exists", error.code)
        self.assertEqual(original, (self.runner.output / "identity.json").read_bytes())

    def test_ci_build_is_not_publishable_and_binds_no_line(self):
        """The pull-request caller builds the same packages without release authority."""
        # Arrange
        os.environ["GITHUB_EVENT_NAME"] = "pull_request"
        os.environ["GITHUB_WORKFLOW_REF"] = f"{common.REPOSITORY}/.github/workflows/ci.yml@refs/pull/7/merge"
        os.environ.pop("NESTEDSET_RELEASE_LINE")

        # Act
        runner = qualification.Qualification(self.repo, self.root / "ci", "10.1.0")

        # Assert
        self.assertIsNone(runner.line)
        self.assertFalse(runner.publishable)
        self.assertEqual(1, common.read_json(runner.output / "identity.json")["schemaVersion"])

    def test_ci_caller_cannot_borrow_a_release_line(self):
        """A frozen grant only binds jobs that the release workflow itself called."""
        # Arrange
        os.environ["GITHUB_WORKFLOW_REF"] = f"{common.REPOSITORY}/.github/workflows/ci.yml@refs/heads/main"
        output = self.root / "borrowed"

        # Act
        error = capture(lambda: qualification.Qualification(self.repo, output, VERSION))

        # Assert
        self.assertIsInstance(error, common.ReleaseError)
        self.assertEqual("line_identity", error.code)
        self.assertFalse(output.exists())

    def test_output_cannot_replace_source(self):
        """Qualification must not write evidence over the source or its parent directory."""
        # Arrange
        output = self.root

        # Act
        error = capture(lambda: qualification.Qualification(self.repo, output, VERSION))

        # Assert
        self.assertIsInstance(error, common.ReleaseError)
        self.assertEqual("output_scope", error.code)


class AssemblyTests(QualificationFixture):
    """Sealing copies verified evidence once and never overwrites a candidate."""

    def test_existing_candidate_is_rejected(self):
        """Assembly never overwrites a candidate from an earlier call."""
        # Arrange
        candidate = self.complete_fixture()
        original = common.file_inventory(candidate)

        # Act
        error = capture(self.runner.assemble)

        # Assert
        self.assertIsInstance(error, common.ReleaseError)
        self.assertEqual("candidate_exists", error.code)
        self.assertEqual(original, common.file_inventory(candidate))

    def test_candidate_contains_this_runs_evidence(self):
        """A complete candidate binds test and command logs to its source identity."""
        # Arrange
        candidate = self.complete_fixture()

        # Act
        with zipfile.ZipFile(candidate / "qualification-evidence.zip") as archive:
            names = set(archive.namelist())

        # Assert
        self.assertIn("identity.json", names)
        self.assertIn("tests/fixture.txt", names)
        self.assertIn("logs/tests/fixture.log", names)
        self.assertEqual(VERSION, common.load_candidate(candidate)["version"])


class SourceAndLockTests(unittest.TestCase):
    """Workspace moves and RC version rebasing preserve the true source/dependency boundary."""

    def test_git_inspection_disables_optional_index_refresh(self):
        """Read-only source inspection overrides inherited Git settings that permit index-cache writes."""
        # Arrange
        process = Mock(returncode=0, stdout=b"clean\n")
        with patch.dict(os.environ, {"GIT_OPTIONAL_LOCKS": "1"}), \
                patch.object(qualification.subprocess, "run", return_value=process) as execute:
            # Act
            result = qualification.git(Path.cwd(), "status", "--porcelain")

        # Assert
        self.assertEqual("clean", result)
        self.assertEqual("0", execute.call_args.kwargs["env"]["GIT_OPTIONAL_LOCKS"])

    def test_staged_move_is_rejected_as_unreviewed_source(self):
        """A deleted index entry reaches the clean-source gate instead of a missing-file error."""
        # Arrange
        with tempfile.TemporaryDirectory() as directory:
            repo = Path(directory)
            text_file(repo / "new.cs", "new location")
            responses = {("ls-files", "--cached", "--others", "--exclude-standard", "-z"): "old.cs\0new.cs\0",
                         ("rev-parse", "--verify", "HEAD"): COMMIT,
                         ("rev-parse", "--verify", "HEAD^{tree}"): TREE,
                         ("status", "--porcelain", "--untracked-files=all"): " D old.cs\n?? new.cs"}

            # Act
            with patch.object(qualification, "git", side_effect=lambda _, *args, **kwargs: responses[args]):
                error = capture(lambda: qualification.source_identity(repo))

            # Assert
            self.assertIsInstance(error, common.ReleaseError)
            self.assertEqual("unreviewed_source", error.code)

    def test_dirty_source_cannot_be_a_normal_candidate(self):
        """Workspace acceptance does not relax the normal clean-source gate."""
        # Arrange
        with tempfile.TemporaryDirectory() as directory:
            values = ["", COMMIT, TREE, " M source.cs"]

            # Act
            with patch.object(qualification, "git", side_effect=values):
                error = capture(lambda: qualification.source_identity(Path(directory)))

            # Assert
            self.assertIsInstance(error, common.ReleaseError)
            self.assertEqual("unreviewed_source", error.code)

    def test_rc_rebase_changes_only_internal_project_edges(self):
        """The release version must not silently reevaluate external dependency records."""
        # Arrange
        original = {"version": 2, "dependencies": {"net10.0": {
            "consumer": {"type": "Project", "dependencies": {"Doka.NestedSet": "[10.1.0-dev, )",
                                                                "ThirdParty": "[10.0.0, )"}},
            "ThirdParty": {"type": "Direct", "requested": "[10.0.0, )", "resolved": "10.0.0",
                           "contentHash": "unchanged", "dependencies": {"Doka.NestedSet": "10.1.0-dev"}}}}}
        untouched = copy.deepcopy(original)

        # Act
        rebased = qualification.rebased_lock(original, VERSION)

        # Assert
        self.assertEqual(untouched, original)
        self.assertEqual(f"[{VERSION}, )", rebased["dependencies"]["net10.0"]["consumer"]["dependencies"]["Doka.NestedSet"])
        self.assertEqual(untouched["dependencies"]["net10.0"]["ThirdParty"], rebased["dependencies"]["net10.0"]["ThirdParty"])
        self.assertEqual("[10.0.0, )", rebased["dependencies"]["net10.0"]["consumer"]["dependencies"]["ThirdParty"])


class PackageLockTests(unittest.TestCase):
    """Qualification requires reviewed shipping locks without imposing locks on every solution project."""

    def setUp(self):
        """Declare every shipping package and a nonshipping test project without a lockfile."""
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.repo = Path(temporary.name) / "repo"
        self.output = Path(temporary.name) / "output"
        solution = ET.Element("Solution")
        self.lock = {"version": 2, "dependencies": {"net10.0": {}}}
        for package in common.PACKAGES:
            project = f"src/{package}/{package}.csproj"
            text_file(self.repo / project, "<Project />")
            common.write_json(self.repo / "src" / package / "packages.lock.json", self.lock)
            ET.SubElement(solution, "Project", Path=project)

        test_project = "tests/Example.Tests/Example.Tests.csproj"
        text_file(self.repo / test_project, "<Project />")
        ET.SubElement(solution, "Project", Path=test_project)
        ET.ElementTree(solution).write(self.repo / qualification.SOLUTION)

    def test_only_shipping_locks_are_prepared(self):
        """An ordinary test project without a committed lock must not block RC preparation."""
        # Arrange
        source_locks = {package: (self.repo / "src" / package / "packages.lock.json").read_bytes()
                        for package in common.PACKAGES}

        # Act
        qualification.prepare_locks(self.repo, self.output, VERSION)
        records = common.read_json(self.output / "quality/dependencies.json")

        # Assert
        self.assertEqual(set(common.PACKAGES), set(records))
        self.assertEqual(len(common.PACKAGES), len(list((self.output / "locks").rglob("packages.lock.json"))))
        self.assertFalse((self.repo / "tests/Example.Tests/packages.lock.json").exists())
        for package, original in source_locks.items():
            self.assertEqual(original, (self.repo / "src" / package / "packages.lock.json").read_bytes())
            self.assertEqual(self.lock, records[package]["value"])

    def test_missing_shipping_lock_is_rejected(self):
        """Reducing the lock scope must not allow an unlocked published package."""
        # Arrange
        (self.repo / "src" / common.PACKAGES[0] / "packages.lock.json").unlink()

        # Act
        error = capture(lambda: qualification.prepare_locks(self.repo, self.output, VERSION))

        # Assert
        self.assertIsInstance(error, common.ReleaseError)
        self.assertEqual("lock_missing", error.code)

    def test_shipping_project_missing_from_solution_is_rejected(self):
        """A lockfile on disk cannot compensate for omitting its shipping project from qualification."""
        # Arrange
        solution = ET.parse(self.repo / qualification.SOLUTION)
        solution.getroot().remove(solution.getroot().find("Project"))
        solution.write(self.repo / qualification.SOLUTION)

        # Act
        error = capture(lambda: qualification.prepare_locks(self.repo, self.output, VERSION))

        # Assert
        self.assertIsInstance(error, common.ReleaseError)
        self.assertEqual("package_project_missing", error.code)

    def test_changed_owned_shipping_lock_is_rejected(self):
        """Restore cannot silently replace a reviewed package graph in the candidate workspace."""
        # Arrange
        qualification.prepare_locks(self.repo, self.output, VERSION)
        changed = copy.deepcopy(self.lock)
        changed["dependencies"]["net10.0"]["Unexpected.Package"] = {"type": "Direct", "resolved": "1.0.0"}
        common.write_json(self.output / "locks" / common.PACKAGES[0] / "packages.lock.json", changed)

        # Act
        error = capture(lambda: qualification.verify_locks(self.output))

        # Assert
        self.assertIsInstance(error, common.ReleaseError)
        self.assertEqual("dependencies_changed", error.code)


class TestResultTests(unittest.TestCase):
    """TRX coverage must include every test project and actual passing cases."""

    def setUp(self):
        """Declare two executable suites and their shared, non-executable specification library."""
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.repo = Path(temporary.name)
        self.results = self.repo / "results"
        self.results.mkdir()
        root = ET.Element("Solution")
        for name in ("First.Tests", "Second.Tests"):
            path = f"tests/{name}/{name}.csproj"
            text_file(self.repo / path, "<Project />")
            ET.SubElement(root, "Project", Path=path)

        library = "tests/Specification.Tests/Specification.Tests.csproj"
        text_file(self.repo / library, "<Project><PropertyGroup><IsTestProject>false</IsTestProject></PropertyGroup></Project>")
        ET.SubElement(root, "Project", Path=library)
        ET.ElementTree(root).write(self.repo / qualification.SOLUTION)

    def report(self, project, *, name=None, outcomes=("Passed",), duplicate_case=False, identity_prefix=""):
        """Write a minimal correctly namespaced TRX with internally coherent counters."""
        namespace = qualification.TRX["t"]
        root = ET.Element("TestRun", xmlns=namespace)
        definitions = ET.SubElement(root, "TestDefinitions")
        results = ET.SubElement(root, "Results")
        for index, outcome in enumerate(outcomes):
            identity = identity_prefix + ("0" if duplicate_case else str(index))
            ET.SubElement(definitions, "UnitTest", id=identity, storage=f"/bin/{project}.dll")
            ET.SubElement(results, "UnitTestResult", testId=identity, executionId=identity, outcome=outcome)

        summary = ET.SubElement(root, "ResultSummary")
        ET.SubElement(summary, "Counters", total=str(len(outcomes)), executed=str(sum(o != "NotExecuted" for o in outcomes)),
                      passed=str(outcomes.count("Passed")), failed=str(outcomes.count("Failed")))
        path = self.results / (name or f"{project}.trx")
        path.parent.mkdir(parents=True, exist_ok=True)
        ET.ElementTree(root).write(path)

        return path

    def plans(self):
        """Retain every promised provider plan family in one test cell, as complete verification requires."""
        for engine in ("MySql", "MariaDb", "PostgreSql", "SqlServer", "Sqlite"):
            text_file(self.results / "cell/query-plans" / f"{engine}-key-filter.txt", "observed key-filter plan")
            if engine != "Sqlite":
                text_file(self.results / "cell/query-plans" / f"{engine}-hierarchy.txt", "observed hierarchy plan")

    def test_complete_results_pass(self):
        """Every executable suite supplies a result; the specification library supplies none."""
        # Arrange
        self.report("First.Tests")
        self.report("Second.Tests")

        # Act
        result = qualification.verify_results(self.repo, self.results)

        # Assert
        self.assertEqual({"First.Tests", "Second.Tests"}, set(result))
        self.assertTrue(all(value["passed"] == 1 for value in result.values()))

    def run_result_command(self, *arguments):
        """Exercise the real entrypoint while forbidding qualification or source initialization."""
        output = io.StringIO()
        errors = io.StringIO()
        with patch.object(qualification, "ROOT", self.repo), \
                patch.object(sys, "argv", ["qualification", *map(str, arguments)]), \
                patch.object(qualification, "Qualification", side_effect=AssertionError("Qualification was initialized")), \
                patch.object(qualification, "source_identity", side_effect=AssertionError("Source identity was inspected")), \
                redirect_stdout(output), redirect_stderr(errors):
            try:
                status = qualification.main()
            except SystemExit as error:
                status = error.code

        return status, output.getvalue(), errors.getvalue()

    def test_result_command_verifies_solution_without_release_initialization(self):
        """The read-only mode needs passing results and solution inventory, without version or output."""
        # Arrange
        self.report("First.Tests")
        self.report("Second.Tests")
        self.plans()
        before = {path.relative_to(self.repo): path.read_bytes() for path in self.repo.rglob("*") if path.is_file()}

        # Act
        status, output, errors = self.run_result_command("--verify-results", self.results)

        # Assert
        self.assertEqual(0, status, errors)
        self.assertEqual({"First.Tests", "Second.Tests"}, set(json.loads(output)))
        self.assertEqual(before, {path.relative_to(self.repo): path.read_bytes()
                                  for path in self.repo.rglob("*") if path.is_file()})

    def test_result_command_selects_known_assembly_name(self):
        """CI may verify one owned assembly without requiring results for other solution projects."""
        # Arrange
        self.report("First.Tests")

        # Act
        status, output, errors = self.run_result_command("--verify-results", self.results, "--project", "First.Tests")

        # Assert
        self.assertEqual(0, status, errors)
        self.assertEqual({"First.Tests"}, set(json.loads(output)))

    def test_result_module_entrypoint_verifies_actual_solution_assembly(self):
        """The exact CI module invocation works in a fresh process with real solution inventory."""
        # Arrange
        unit = qualification.ALLOCATION_PROJECT
        self.report(unit, name="allocations.trx", identity_prefix="budget-")
        self.report(unit, name="coverage.trx", identity_prefix="covered-")
        command = [sys.executable, "-m", "eng.release.qualification", "--verify-results", str(self.results),
                   "--project", unit]

        # Act
        result = subprocess.run(command, cwd=qualification.ROOT, capture_output=True, text=True, check=False)

        # Assert
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(2, json.loads(result.stdout)[unit]["passed"])

    def test_result_command_accepts_complementary_unit_partitions(self):
        """The same CLI used by CI retains the mandatory disjoint Unit result contract."""
        # Arrange
        unit = self.unit_project()
        self.report(unit, name="allocations.trx", identity_prefix="budget-")
        self.report(unit, name="coverage.trx", identity_prefix="covered-")
        self.plans()

        # Act
        status, output, errors = self.run_result_command("--verify-results", self.results)

        # Assert
        self.assertEqual(0, status, errors)
        self.assertEqual(2, json.loads(output)[unit]["passed"])

    def test_result_command_rejects_missing_unit_partition(self):
        """A passing covered suite cannot replace the missing uninstrumented budget results."""
        # Arrange
        unit = self.unit_project()
        self.report(unit, name="coverage.trx")

        # Act
        status, output, errors = self.run_result_command("--verify-results", self.results)

        # Assert
        self.assertEqual(1, status)
        self.assertEqual("", output)
        self.assertIn("tests_partitions", errors)

    def test_result_command_rejects_missing_provider_plans_for_complete_results(self):
        """Complete verification requires the live plan families, not only passing reports."""
        # Arrange
        self.report("First.Tests")
        self.report("Second.Tests")

        # Act
        status, output, errors = self.run_result_command("--verify-results", self.results)

        # Assert
        self.assertEqual(1, status)
        self.assertEqual("", output)
        self.assertIn("query_plans_missing", errors)

    def test_result_command_rejects_missing_solution_project(self):
        """Without a selector the command requires evidence for every executable solution test."""
        # Arrange
        self.report("First.Tests")

        # Act
        status, output, errors = self.run_result_command("--verify-results", self.results)

        # Assert
        self.assertEqual(1, status)
        self.assertEqual("", output)
        self.assertIn("tests_missing", errors)

    def test_result_command_rejects_overlapping_unit_partitions(self):
        """The command cannot count a repeated Unit case as complementary coverage."""
        # Arrange
        unit = self.unit_project()
        self.report(unit, name="allocations.trx")
        self.report(unit, name="coverage.trx")

        # Act
        status, output, errors = self.run_result_command("--verify-results", self.results)

        # Assert
        self.assertEqual(1, status)
        self.assertEqual("", output)
        self.assertIn("tests_duplicate", errors)

    def test_result_command_rejects_failed_case(self):
        """A failed result is rejected by the entrypoint before a success summary can be printed."""
        # Arrange
        self.report("First.Tests", outcomes=("Failed",))

        # Act
        status, output, errors = self.run_result_command("--verify-results", self.results, "--project", "First.Tests")

        # Assert
        self.assertEqual(1, status)
        self.assertEqual("", output)
        self.assertIn("tests_not_passed", errors)

    def test_result_command_rejects_skipped_case(self):
        """A skipped result is not accepted merely because its other counters are coherent."""
        # Arrange
        self.report("First.Tests", outcomes=("Passed", "NotExecuted"))

        # Act
        status, output, errors = self.run_result_command("--verify-results", self.results, "--project", "First.Tests")

        # Assert
        self.assertEqual(1, status)
        self.assertEqual("", output)
        self.assertIn("tests_not_passed", errors)

    def test_result_command_rejects_unknown_project(self):
        """A matching foreign TRX cannot authorize a name outside executable solution inventory."""
        # Arrange
        self.report("Foreign.Tests")

        # Act
        status, output, errors = self.run_result_command("--verify-results", self.results, "--project", "Foreign.Tests")

        # Assert
        self.assertEqual(1, status)
        self.assertEqual("", output)
        self.assertIn("tests_identity", errors)

    def test_result_command_rejects_non_executable_project(self):
        """A shared specification library cannot be selected as a result-producing test project."""
        # Arrange
        self.report("Specification.Tests")

        # Act
        status, output, errors = self.run_result_command("--verify-results", self.results,
                                                         "--project", "Specification.Tests")

        # Assert
        self.assertEqual(1, status)
        self.assertEqual("", output)
        self.assertIn("tests_identity", errors)

    def test_result_command_rejects_qualification_arguments(self):
        """Each release-only option is incompatible with a read-only result check."""
        variants = (("--version", VERSION), ("--output", "unused"), ("--job", "build"))
        for arguments in variants:
            with self.subTest(arguments=arguments):
                # Arrange
                command = ("--verify-results", self.results, *arguments)

                # Act
                status, output, errors = self.run_result_command(*command)

                # Assert
                self.assertEqual(2, status)
                self.assertEqual("", output)
                self.assertIn("incompatible with --verify-results", errors)

    def test_qualification_still_requires_version(self):
        """Adding the results-only mode does not make release version optional for qualification."""
        # Arrange
        arguments = ("--job", "build", "--output", "unused")

        # Act
        status, output, errors = self.run_result_command(*arguments)

        # Assert
        self.assertEqual(2, status)
        self.assertEqual("", output)
        self.assertIn("--version", errors)

    def test_qualification_still_requires_output(self):
        """Ordinary qualification cannot silently choose a new output location."""
        # Arrange
        arguments = ("--job", "build", "--version", VERSION)

        # Act
        status, output, errors = self.run_result_command(*arguments)

        # Assert
        self.assertEqual(2, status)
        self.assertEqual("", output)
        self.assertIn("--output", errors)

    def test_qualification_requires_an_explicit_job(self):
        """Version and output alone no longer select an implicit complete local run."""
        # Arrange
        arguments = ("--version", VERSION, "--output", "unused")

        # Act
        status, output, errors = self.run_result_command(*arguments)

        # Assert
        self.assertEqual(2, status)
        self.assertEqual("", output)
        self.assertIn("--job", errors)

    def test_build_and_seal_dispatch_to_their_fixed_operations(self):
        """Only the seal restores the producer identity; the build starts from a fresh output."""
        for job, existing in (("build", False), ("seal", True)):
            with self.subTest(job=job):
                # Arrange
                output = self.repo / job
                arguments = ["qualification", "--job", job, "--version", VERSION, "--output", str(output)]
                runner = Mock()

                # Act
                with patch.object(qualification, "ROOT", self.repo), patch.object(sys, "argv", arguments), \
                        patch.object(qualification, "Qualification", return_value=runner) as constructor:
                    status = qualification.main()

                # Assert
                self.assertEqual(0, status)
                constructor.assert_called_once_with(self.repo, output, VERSION, existing=existing)
                getattr(runner, job).assert_called_once_with()

    def unit_project(self):
        """Include the one assembly whose allocation and coverage partitions are complementary."""
        name = "Doka.EntityFrameworkCore.NestedSet.Unit.Tests"
        path = f"tests/{name}/{name}.csproj"
        text_file(self.repo / path, "<Project />")
        solution = ET.parse(self.repo / qualification.SOLUTION)
        ET.SubElement(solution.getroot(), "Project", Path=path)
        solution.write(self.repo / qualification.SOLUTION)
        self.report("First.Tests")
        self.report("Second.Tests")

        return name

    def test_complementary_unit_partitions_are_combined(self):
        """Two disjoint fixed Unit partitions retain every result and both artifact hashes."""
        # Arrange
        unit = self.unit_project()
        self.report(unit, name="allocations.trx", identity_prefix="budget-")
        self.report(unit, name="coverage.trx", identity_prefix="covered-")

        # Act
        result = qualification.verify_results(self.repo, self.results)

        # Assert
        self.assertEqual(2, result[unit]["passed"])
        self.assertEqual({"allocations.trx", "coverage.trx"},
                         {report["trx"] for report in result[unit]["reports"]})
        self.assertTrue(all(report["sha256"] for report in result[unit]["reports"]))

    def test_missing_allocation_partition_is_rejected(self):
        """An instrumented success cannot hide absent uninstrumented budgets."""
        # Arrange
        unit = self.unit_project()
        self.report(unit, name="coverage.trx")

        # Act
        error = capture(lambda: qualification.verify_results(self.repo, self.results))

        # Assert
        self.assertEqual("tests_partitions", error.code)

    def test_missing_coverage_partition_is_rejected(self):
        """Passing budgets cannot replace the instrumented semantic tests."""
        # Arrange
        unit = self.unit_project()
        self.report(unit, name="allocations.trx")

        # Act
        error = capture(lambda: qualification.verify_results(self.repo, self.results))

        # Assert
        self.assertEqual("tests_partitions", error.code)

    def test_duplicate_identity_across_unit_partitions_is_rejected(self):
        """A filtering mistake cannot run the same case in both modes and inflate totals."""
        # Arrange
        unit = self.unit_project()
        self.report(unit, name="allocations.trx")
        self.report(unit, name="coverage.trx")

        # Act
        error = capture(lambda: qualification.verify_results(self.repo, self.results))

        # Assert
        self.assertEqual("tests_duplicate", error.code)

    def test_unknown_unit_partition_is_rejected(self):
        """Additional or renamed reports cannot satisfy the fixed measurement contract."""
        # Arrange
        unit = self.unit_project()
        self.report(unit, name="allocations.trx", identity_prefix="budget-")
        self.report(unit, name="coverage.trx", identity_prefix="covered-")
        self.report(unit, name="extra.trx", identity_prefix="extra-")

        # Act
        error = capture(lambda: qualification.verify_results(self.repo, self.results))

        # Assert
        self.assertEqual("tests_partitions", error.code)

    def test_disjoint_project_partitions_are_combined(self):
        """Complementary filtered cells of one project add up without repeating a case."""
        # Arrange
        self.report("First.Tests", name="engine-a/coverage.trx", identity_prefix="a-")
        self.report("First.Tests", name="engine-b/coverage.trx", identity_prefix="b-")
        self.report("Second.Tests")

        # Act
        result = qualification.verify_results(self.repo, self.results)

        # Assert
        self.assertEqual(2, result["First.Tests"]["passed"])
        self.assertEqual({"engine-a/coverage.trx", "engine-b/coverage.trx"},
                         {report["trx"] for report in result["First.Tests"]["reports"]})

    def test_overlapping_project_partitions_are_rejected(self):
        """A filter mistake that runs one case in two cells cannot inflate the result."""
        # Arrange
        self.report("First.Tests", name="engine-a/coverage.trx", identity_prefix="same-")
        self.report("First.Tests", name="engine-b/coverage.trx", identity_prefix="same-")
        self.report("Second.Tests")

        # Act
        error = capture(lambda: qualification.verify_results(self.repo, self.results))

        # Assert
        self.assertEqual("tests_duplicate", error.code)

    def test_library_report_cannot_replace_executable_suite(self):
        """A result attributed to a non-test library cannot satisfy missing executable coverage."""
        # Arrange
        self.report("First.Tests")
        self.report("Specification.Tests")

        # Act
        error = capture(lambda: qualification.verify_results(self.repo, self.results))

        # Assert
        self.assertIsInstance(error, common.ReleaseError)
        self.assertEqual("tests_identity", error.code)

    def test_missing_project_is_rejected(self):
        """One green project cannot stand in for a missing suite."""
        # Arrange
        self.report("First.Tests")

        # Act
        error = capture(lambda: qualification.verify_results(self.repo, self.results))

        # Assert
        self.assertIsInstance(error, common.ReleaseError)
        self.assertEqual("tests_missing", error.code)

    def test_skipped_case_is_rejected(self):
        """A partially skipped suite is not full qualification."""
        # Arrange
        self.report("First.Tests", outcomes=("Passed", "NotExecuted"))
        self.report("Second.Tests")

        # Act
        error = capture(lambda: qualification.verify_results(self.repo, self.results))

        # Assert
        self.assertIsInstance(error, common.ReleaseError)
        self.assertEqual("tests_not_passed", error.code)

    def test_duplicate_project_report_is_rejected(self):
        """Duplicate project artifacts cannot make attempt selection ambiguous."""
        # Arrange
        self.report("First.Tests")
        self.report("First.Tests", name="duplicate.trx")
        self.report("Second.Tests")

        # Act
        error = capture(lambda: qualification.verify_results(self.repo, self.results))

        # Assert
        self.assertIsInstance(error, common.ReleaseError)
        self.assertEqual("tests_duplicate", error.code)

    def test_zero_test_success_is_rejected(self):
        """Empty result output cannot satisfy test qualification."""
        # Arrange
        self.report("First.Tests", outcomes=())
        self.report("Second.Tests")

        # Act
        error = capture(lambda: qualification.verify_results(self.repo, self.results))

        # Assert
        self.assertIsInstance(error, common.ReleaseError)
        self.assertEqual("tests_empty", error.code)

    def test_duplicate_case_identity_is_rejected(self):
        """Repeated copies of one passing result must not inflate the observed test count."""
        # Arrange
        self.report("First.Tests", outcomes=("Passed", "Passed"), duplicate_case=True)
        self.report("Second.Tests")

        # Act
        error = capture(lambda: qualification.verify_results(self.repo, self.results))

        # Assert
        self.assertIsInstance(error, common.ReleaseError)


class QueryPlanEvidenceTests(unittest.TestCase):
    """The full provider run must retain every promised live plan family."""

    def setUp(self):
        """Create four server hierarchy plans and five key-filter plans across two test cells."""
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.directory = Path(temporary.name)
        for engine in ("MySql", "MariaDb", "PostgreSql", "SqlServer"):
            text_file(self.directory / "servers/query-plans" / f"{engine}-hierarchy.txt", "observed hierarchy plan")
        for engine in ("MySql", "MariaDb", "PostgreSql", "SqlServer", "Sqlite"):
            text_file(self.directory / "filters/query-plans" / f"{engine}-key-filter.txt", "observed key-filter plan")

    def test_complete_plan_inventory_is_accepted(self):
        """All promised engine/family pairs are present and nonempty."""
        # Arrange
        directory = self.directory

        # Act
        error = capture(lambda: qualification.verify_query_plans(directory))

        # Assert
        self.assertIsNone(error)

    def test_same_plan_name_from_two_cells_is_accepted(self):
        """Cells that partition one provider project keep their own copy of a shared plan name."""
        # Arrange
        text_file(self.directory / "capacity/query-plans/MySql-hierarchy.txt", "another cell's observed plan")

        # Act
        error = capture(lambda: qualification.verify_query_plans(self.directory))

        # Assert
        self.assertIsNone(error)

    def test_plan_outside_a_test_cell_is_not_evidence(self):
        """Loose files at the result root cannot replace a cell's retained plan."""
        # Arrange
        source = self.directory / "servers/query-plans/MariaDb-hierarchy.txt"
        text_file(self.directory / "MariaDb-hierarchy.txt", source.read_text())
        source.unlink()

        # Act
        error = capture(lambda: qualification.verify_query_plans(self.directory))

        # Assert
        self.assertEqual("query_plans_missing", error.code)

    def test_missing_server_plan_is_rejected(self):
        """A passing engine fixture does not replace its absent query-plan evidence."""
        # Arrange
        (self.directory / "servers/query-plans/MariaDb-hierarchy.txt").unlink()

        # Act
        error = capture(lambda: qualification.verify_query_plans(self.directory))

        # Assert
        self.assertIsInstance(error, common.ReleaseError)
        self.assertEqual("query_plans_missing", error.code)

    def test_empty_sqlite_plan_is_rejected(self):
        """An empty file cannot claim SQLite query-plan qualification."""
        # Arrange
        text_file(self.directory / "filters/query-plans/Sqlite-key-filter.txt", "")

        # Act
        error = capture(lambda: qualification.verify_query_plans(self.directory))

        # Assert
        self.assertIsInstance(error, common.ReleaseError)
        self.assertEqual("query_plans_missing", error.code)


class ProcessDeadlineTests(QualificationFixture):
    """Tiny local processes exercise timeout evidence and descendant cleanup without external services."""

    def test_nonzero_command_retains_failure_evidence(self):
        """A failed command preserves its output and exit code without producing a candidate."""
        # Arrange
        command = [sys.executable, "-c", "print('fixture failure'); raise SystemExit(7)"]

        # Act
        error = capture(lambda: self.runner.command("quality", "failure", command))

        # Assert
        self.assertIsInstance(error, common.ReleaseError)
        self.assertEqual("command_failed", error.code)
        self.assertEqual("7\n", (self.runner.output / "logs/quality/failure.failed").read_text())
        self.assertIn("fixture failure", (self.runner.output / "logs/quality/failure.log").read_text())

    def test_timeout_stops_child_that_ignores_termination(self):
        """The process group must stop even when its leader exits before a stubborn descendant."""
        # Arrange
        heartbeat = self.root / "heartbeat"
        pid_file = self.root / "child.pid"
        child = ("import pathlib,signal,time; signal.signal(signal.SIGTERM,signal.SIG_IGN); "
                 f"p=pathlib.Path({str(heartbeat)!r}); "
                 "exec('while True:\\n p.write_text(str(time.monotonic_ns()))\\n time.sleep(0.01)')")
        parent = (f"import pathlib,subprocess,sys,time; p=subprocess.Popen([sys.executable,'-c',{child!r}]); "
                  f"pathlib.Path({str(pid_file)!r}).write_text(str(p.pid)); time.sleep(20)")
        self.runner.deadline = time.monotonic() + 0.7

        # Act
        error = capture(lambda: self.runner.command("quality", "timeout", [sys.executable, "-c", parent]))
        first = heartbeat.read_bytes() if heartbeat.exists() else None
        time.sleep(0.08)
        second = heartbeat.read_bytes() if heartbeat.exists() else None
        if pid_file.exists():
            try:
                os.kill(int(pid_file.read_text()), signal.SIGKILL)
            except ProcessLookupError:
                pass

        # Assert
        self.assertIsInstance(error, common.ReleaseError)
        self.assertEqual("deadline", error.code)
        self.assertIsNotNone(first, "The child must actually start before testing cleanup")
        self.assertEqual(first, second, "The owned child continued writing after qualification timed out")


if __name__ == "__main__":
    unittest.main()

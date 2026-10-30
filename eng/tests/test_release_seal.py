"""Offline RC regressions for sealing exact packages with the shared workflow's evidence."""

import copy
import io
import os
import signal
import shutil
import sys
import time
import unittest
import xml.etree.ElementTree as ET
import zipfile
from contextlib import contextmanager, redirect_stdout, suppress
from pathlib import Path
from unittest.mock import patch

from eng.release import common, qualification
from eng.tests.test_release_qualification import (
    COMMIT,
    VERSION,
    QualificationFixture,
    capture,
    stage_fixture,
    text_file,
)

PROJECTS = ("First.Tests", "Second.Tests")


class SealFixture(QualificationFixture):
    """Declare a small solution and downloaded evidence without invoking .NET, Git, or Docker."""

    def setUp(self):
        """Include executable tests and the shared specification library."""
        super().setUp()
        solution = ET.Element("Solution")
        for name in (*PROJECTS, "Specification.Tests"):
            project = f"tests/{name}/{name}.csproj"
            is_test = "false" if name == "Specification.Tests" else "true"
            text_file(self.repo / project,
                      f"<Project><PropertyGroup><IsTestProject>{is_test}</IsTestProject></PropertyGroup></Project>")
            ET.SubElement(solution, "Project", Path=project)

        ET.ElementTree(solution).write(self.repo / qualification.SOLUTION)
        stage_fixture(self.runner)

    def restored(self, **arguments):
        """Construct the seal against the existing build identity."""
        return qualification.Qualification(self.repo, self.runner.output, arguments.pop("version", VERSION),
                                           existing=True, **arguments)

    def report(self, cell, project, *, identity="1"):
        """Write one test cell's internally bound passing case and a nonempty coverage artifact."""
        root = ET.Element("TestRun", xmlns=qualification.TRX["t"])
        definitions = ET.SubElement(root, "TestDefinitions")
        ET.SubElement(definitions, "UnitTest", id=identity, storage=f"/bin/{project}.dll")
        results = ET.SubElement(root, "Results")
        ET.SubElement(results, "UnitTestResult", testId=identity, executionId=identity, outcome="Passed")
        summary = ET.SubElement(root, "ResultSummary")
        ET.SubElement(summary, "Counters", total="1", executed="1", passed="1", failed="0")
        directory = self.runner.output / "tests" / cell
        directory.mkdir(parents=True, exist_ok=True)
        ET.ElementTree(root).write(directory / "coverage.trx")
        text_file(directory / "coverage.cobertura.xml", "<coverage />")

        return directory

    def fake_command(self):
        """Retain synthetic command logs while leaving result validation and assembly real."""
        def execute(stage, name, arguments, env=None):
            text_file(self.runner.output / "logs" / stage / f"{name}.log", "fixture command passed\n")

        return patch.object(self.runner, "command", side_effect=execute)


class RestoredIdentityTests(SealFixture):
    """The seal must retain the build's source, version, and workflow attempt."""

    def test_matching_identity_is_restored_without_rewriting_it(self):
        """A valid seal uses the exact producer identity bytes."""
        # Arrange
        original = (self.runner.output / "identity.json").read_bytes()

        # Act
        restored = self.restored()

        # Assert
        self.assertEqual(self.runner.identity, restored.identity)
        self.assertEqual(original, (self.runner.output / "identity.json").read_bytes())

    def test_wrong_commit_version_or_attempt_is_rejected(self):
        """Similar packages from another producer cannot join the current attempt."""
        # Arrange
        different = copy.deepcopy(self.source)
        different["commit"] = "d" * 40
        cases = (
            (patch.object(qualification, "source_identity", return_value=different), {}, different["commit"]),
            (patch.dict(os.environ, {"GITHUB_RUN_ATTEMPT": "2"}), {}, COMMIT),
            (patch.dict(os.environ, {}), {"version": "10.1.0-rc.2"}, COMMIT),
        )

        for context, arguments, commit in cases:
            with self.subTest(arguments=arguments, commit=commit), context, \
                    patch.dict(os.environ, {"GITHUB_SHA": commit}):
                # Act
                error = capture(lambda: self.restored(**arguments))

                # Assert
                self.assertIsInstance(error, common.ReleaseError)
                self.assertEqual("build_identity", error.code)


class PackageInputTests(SealFixture):
    """Downloaded packages must be the inspected producer bytes."""

    def test_executable_inventory_excludes_the_specification_library(self):
        """Shared test sources do not create another required result."""
        # Act
        projects = qualification.test_projects(self.repo)

        # Assert
        self.assertEqual(set(PROJECTS), {path.stem for path in projects})

    def test_primary_and_symbol_mutation_is_rejected(self):
        """Neither package archive may differ from the producer's inspected digest."""
        # Arrange
        for extension in ("nupkg", "snupkg"):
            path = self.runner.output / "packages" / f"{common.PACKAGES[0]}.{VERSION}.{extension}"
            original = path.read_bytes()
            text_file(path, "changed package bytes")

            # Act
            with self.subTest(extension=extension):
                error = capture(self.runner.verify_package_inputs)
                path.write_bytes(original)

                # Assert
                self.assertEqual("package_inputs", error.code)


class SealTests(SealFixture):
    """Downloaded shared evidence must contain every required result before sealing."""

    def setUp(self):
        """Populate per-cell test results, one project split across two cells, plans, and consumer success."""
        super().setUp()
        self.report("first-engine-a", "First.Tests", identity="a")
        self.report("first-engine-b", "First.Tests", identity="b")
        self.report("second", "Second.Tests")
        plans = self.runner.output / "tests/first-engine-a/query-plans"
        for engine in ("MySql", "MariaDb", "PostgreSql", "SqlServer", "Sqlite"):
            text_file(plans / f"{engine}-key-filter.txt", "key filter")
            if engine != "Sqlite":
                text_file(plans / f"{engine}-hierarchy.txt", "hierarchy")

        packages = common.read_json(self.runner.output / "packages/package-manifest.json")["packages"]
        common.write_json(self.runner.output / "consumer/result.json",
                          {"success": True, "version": VERSION,
                           "consumers": [{"name": name, "success": True} for name, _ in common.CONSUMERS],
                           "candidateSha256": {record["id"]: record["nupkgSha256"] for record in packages}})

    def seal_error(self):
        """Exercise real result verification and assembly; mock external coverage/SBOM commands only."""
        with self.fake_command(), redirect_stdout(io.StringIO()):
            return capture(self.runner.seal)

    def test_complete_shared_evidence_seals_existing_packages(self):
        """Partitioned cells and the built packages form one candidate without another receipt schema."""
        # Arrange
        package = self.runner.output / "packages" / f"{common.PACKAGES[0]}.{VERSION}.nupkg"
        digest = common.sha256(package)

        # Act
        error = self.seal_error()

        # Assert
        self.assertIsNone(error)
        candidate = self.runner.output / "candidate"
        self.assertEqual(COMMIT, common.load_candidate(candidate)["source"]["commit"])
        self.assertEqual(digest, common.sha256(candidate / package.name))
        self.assertEqual(2, common.read_json(self.runner.output / "tests/results.json")["First.Tests"]["passed"])
        with zipfile.ZipFile(candidate / "qualification-evidence.zip") as archive:
            self.assertIn("tests/first-engine-b/coverage.trx", archive.namelist())
            self.assertFalse(any(name.endswith("receipt.json") for name in archive.namelist()))

    def test_shared_plan_name_from_another_cell_does_not_prevent_sealing(self):
        """Two cells of one provider project may retain the same plan name side by side."""
        # Arrange
        text_file(self.runner.output / "tests/first-engine-b/query-plans/MySql-hierarchy.txt", "another cell")

        # Act
        error = self.seal_error()

        # Assert
        self.assertIsNone(error)

    def test_missing_project_or_plan_rejects_partial_artifacts(self):
        """A download pattern may match fewer artifacts; complete result verification must reject it."""
        # Arrange
        cases = (("tests/second/coverage.trx", "tests_missing"),
                 ("tests/first-engine-a/query-plans/MariaDb-hierarchy.txt", "query_plans_missing"))
        for relative, code in cases:
            path = self.runner.output / relative
            original = path.read_bytes()
            path.unlink()
            with self.subTest(relative=relative):
                # Act
                error = self.seal_error()

                # Assert
                self.assertEqual(code, error.code)
                self.assertFalse((self.runner.output / "candidate").exists())

            path.write_bytes(original)

    def test_overlapping_cells_reject_sealing(self):
        """A repeated case in two cells of one project cannot inflate the qualified result."""
        # Arrange
        self.report("first-engine-b", "First.Tests", identity="a")

        # Act
        error = self.seal_error()

        # Assert
        self.assertEqual("tests_duplicate", error.code)
        self.assertFalse((self.runner.output / "candidate").exists())

    def test_ci_identity_cannot_seal(self):
        """Packages built for pull-request CI carry no release line and cannot become a candidate."""
        # Arrange
        self.runner.line = None

        # Act
        error = self.seal_error()

        # Assert
        self.assertEqual("line_identity", error.code)
        self.assertFalse((self.runner.output / "candidate").exists())

    def test_false_wrong_version_or_incomplete_consumer_is_rejected(self):
        """Existing consumer success must name every application and the selected version."""
        # Arrange
        path = self.runner.output / "consumer/result.json"
        original = common.read_json(path)
        cases = ({"success": False}, {"version": "10.1.0-rc.2"}, {"consumers": [{"name": "core"}]})
        for change in cases:
            common.write_json(path, {**original, **change})
            with self.subTest(change=change):
                # Act
                error = self.seal_error()

                # Assert
                self.assertEqual("consumer_result", error.code)
                self.assertFalse((self.runner.output / "candidate").exists())

    def test_missing_or_different_consumer_package_hashes_prevent_sealing(self):
        """Consumer packages and sealed build packages must be the same bytes, not just the same version."""
        # Arrange
        path = self.runner.output / "consumer/result.json"
        original = common.read_json(path)
        hashes = original["candidateSha256"]
        cases = (None, {common.PACKAGES[0]: hashes[common.PACKAGES[0]]},
                 {**hashes, common.PACKAGES[0]: "f" * 64},
                 {common.PACKAGES[0]: hashes[common.PACKAGES[1]],
                  common.PACKAGES[1]: hashes[common.PACKAGES[0]]},
                 {**hashes, "Unexpected.Package": "a" * 64})
        for value in cases:
            shutil.rmtree(self.runner.output / "candidate", ignore_errors=True)
            changed = {**original, "candidateSha256": value}
            if value is None:
                changed.pop("candidateSha256")

            common.write_json(path, changed)
            with self.subTest(hashes=value):
                # Act
                error = self.seal_error()

                # Assert
                self.assertIsInstance(error, common.ReleaseError)
                self.assertEqual("consumer_packages", error.code)
                self.assertFalse((self.runner.output / "candidate").exists())

    def test_offline_sbom_rejection_prevents_sealing(self):
        """Sealing reuses the existing offline verifier and honors its failed process result."""
        # Arrange
        commands = []
        def execute(stage, name, arguments, env=None):
            commands.append(arguments)
            if name == "verify" and stage == "sbom":
                raise common.ReleaseError("command_failed", "invalid retained SBOM")

        # Act
        with patch.object(self.runner, "command", side_effect=execute):
            error = capture(self.runner.seal)

        # Assert
        self.assertEqual("command_failed", error.code)
        self.assertIn("eng/verify-package-sbom.py", commands[-1])
        self.assertIn("verify", commands[-1])
        self.assertFalse((self.runner.output / "candidate").exists())


class StreamingCommandTests(QualificationFixture):
    """Tiny local processes exercise retained and operator-visible command output."""

    def test_success_streams_stdout_and_stderr_into_the_retained_log(self):
        """A successful command exposes progress and warnings without waiting for artifact assembly."""
        # Arrange
        acknowledgement = self.root / "acknowledgement"

        class LiveOutput(io.StringIO):
            """Acknowledge progress while the child is waiting, not after it exits."""

            def write(self, text):
                """Unblock the child only when its progress reaches the operator stream."""
                if "progress-ready" in text:
                    text_file(acknowledgement, "observed live")

                return super().write(text)

        output = LiveOutput()
        # WHY: Post-exit replay cannot create the acknowledgement before the child's deadline.
        script = ("import pathlib,sys,time; "
                  f"ack=pathlib.Path({str(acknowledgement)!r}); "
                  "print('progress-ready', flush=True); deadline=time.monotonic()+3; "
                  "exec('while not ack.exists() and time.monotonic()<deadline:\\n time.sleep(0.01)'); "
                  "print('warning', file=sys.stderr); sys.exit(0 if ack.exists() else 9)")
        command = [sys.executable, "-c", script]

        # Act
        with redirect_stdout(output):
            self.runner.command("quality", "success", command)

        # Assert
        log = (self.runner.output / "logs/quality/success.log").read_text()
        self.assertIn("progress-ready", log)
        self.assertIn("warning", log)
        self.assertIn("progress-ready", output.getvalue())
        self.assertIn("warning", output.getvalue())
        self.assertTrue(acknowledgement.exists())

    def test_failure_streams_output_and_preserves_the_exit_code(self):
        """Live output never turns a nonzero process exit into a passing job."""
        # Arrange
        output = io.StringIO()
        command = [sys.executable, "-c", "print('failed progress'); raise SystemExit(7)"]

        # Act
        with redirect_stdout(output):
            error = capture(lambda: self.runner.command("quality", "failure", command))

        # Assert
        self.assertEqual("command_failed", error.code)
        self.assertIn("failed progress", output.getvalue())
        self.assertEqual("7\n", (self.runner.output / "logs/quality/failure.failed").read_text())
        self.assertIn("failed progress", (self.runner.output / "logs/quality/failure.log").read_text())

    def test_exited_leader_cannot_leave_a_descendant_holding_stdout(self):
        """The owned process group must stop when its leader exits with an inherited output pipe open."""
        # Arrange
        heartbeat = self.root / "heartbeat"
        pid_file = self.root / "child.pid"
        child = ("import os,pathlib,signal,time; signal.signal(signal.SIGTERM,signal.SIG_IGN); "
                 f"p=pathlib.Path({str(heartbeat)!r}); pathlib.Path({str(pid_file)!r}).write_text(str(os.getpid())); "
                 "exec('while True:\\n p.write_text(str(time.monotonic_ns()))\\n time.sleep(0.01)')")
        leader = ("import pathlib,subprocess,sys,time; "
                  f"subprocess.Popen([sys.executable,'-c',{child!r}]); p=pathlib.Path({str(heartbeat)!r}); "
                  "deadline=time.monotonic()+3; "
                  "exec('while not p.exists() and time.monotonic()<deadline:\\n time.sleep(0.01)')")
        self.runner.deadline = time.monotonic() + 0.7

        # Act
        try:
            with redirect_stdout(io.StringIO()):
                error = capture(lambda: self.runner.command("quality", "orphan-output", [sys.executable, "-c", leader]))
            first = heartbeat.read_bytes() if heartbeat.exists() else None
            time.sleep(0.08)
            second = heartbeat.read_bytes() if heartbeat.exists() else None
        finally:
            # WHY: The red regression must not itself leave an unowned descendant running.
            if pid_file.exists():
                with suppress(ProcessLookupError):
                    os.kill(int(pid_file.read_text()), signal.SIGKILL)

        # Assert
        self.assertIsInstance(error, common.ReleaseError)
        self.assertEqual("command_output", error.code)
        self.assertIsNotNone(first, "The descendant must start before exercising group cleanup")
        self.assertEqual(first, second, "The descendant kept writing after the command returned")

    def test_log_write_failure_rejects_an_otherwise_successful_process(self):
        """A reader-thread disk failure cannot turn a zero-exit process into verified output."""
        # Arrange
        original_open = Path.open

        class BrokenLog:
            """Fail the retained log write while leaving process creation and pipe reading real."""

            def write(self, text):
                """Inject the storage failure encountered by the command's output reader."""
                raise OSError("injected log write failure")

            def flush(self):
                """Match the log writer's interface after a successful write."""
                return None

        @contextmanager
        def open_log(path, *arguments, **keywords):
            """Replace only the tested log stream; identity and other evidence files remain real."""
            with original_open(path, *arguments, **keywords) as stream:
                yield BrokenLog() if path.name == "lost-output.log" else stream

        command = [sys.executable, "-c", "print('output must be retained', flush=True)"]

        # Act
        with patch.object(Path, "open", open_log), redirect_stdout(io.StringIO()):
            error = capture(lambda: self.runner.command("quality", "lost-output", command))

        # Assert
        self.assertIsInstance(error, common.ReleaseError)
        self.assertEqual("command_output", error.code)


if __name__ == "__main__":
    unittest.main()

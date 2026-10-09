"""Offline RC regressions for shared deadlines, prebuilt jobs, and complete evidence aggregation."""

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
SAMPLES = ("FileSystem", "Kpis", "UserGroups")


class ParallelFixture(QualificationFixture):
    """Declare a small solution and producer files without invoking .NET, Git, or Docker."""

    def setUp(self):
        """Include executable tests, a specification library, and every runnable sample."""
        super().setUp()
        solution = ET.Element("Solution")
        for name in (*PROJECTS, "Specification.Tests"):
            project = f"tests/{name}/{name}.csproj"
            is_test = "false" if name == "Specification.Tests" else "true"
            text_file(self.repo / project,
                      f"<Project><PropertyGroup><IsTestProject>{is_test}</IsTestProject></PropertyGroup></Project>")
            ET.SubElement(solution, "Project", Path=project)

        for name in SAMPLES:
            project = f"samples/{name}/{name}.csproj"
            text_file(self.repo / project, "<Project />")
            ET.SubElement(solution, "Project", Path=project)

        ET.ElementTree(solution).write(self.repo / qualification.SOLUTION)
        stage_fixture(self.runner)
        for name in (*PROJECTS, *SAMPLES):
            text_file(self.runner.output / "build/bin" / name / "release" / f"{name}.dll", name)

        text_file(self.collector / "Microsoft.VisualStudio.TraceDataCollector.dll", "managed collector")
        text_file(self.collector / "runtimes/linux-x64/native/libcoverage.so", "native engine")
        common.write_json(self.runner.output / "quality/runtime.json",
                          qualification.runtime_inventory(self.runner.output))

    @property
    def collector(self):
        """Locate the collector bytes transported alongside the producer's binaries."""
        return self.runner.output / "build/bin/.coverage-collector"

    def restored(self, **arguments):
        """Construct a consumer against the existing producer identity."""
        return qualification.Qualification(self.repo, self.runner.output, arguments.pop("version", VERSION),
                                           existing=True, **arguments)

    def report(self, runner, project):
        """Write one internally bound passing test case and a nonempty coverage artifact."""
        root = ET.Element("TestRun", xmlns=qualification.TRX["t"])
        definitions = ET.SubElement(root, "TestDefinitions")
        ET.SubElement(definitions, "UnitTest", id="1", storage=f"/bin/{project}.dll")
        results = ET.SubElement(root, "Results")
        ET.SubElement(results, "UnitTestResult", testId="1", executionId="1", outcome="Passed")
        summary = ET.SubElement(root, "ResultSummary")
        ET.SubElement(summary, "Counters", total="1", executed="1", passed="1", failed="0")
        directory = runner.output / "tests" / project
        directory.mkdir(parents=True, exist_ok=True)
        ET.ElementTree(root).write(directory / f"{project}.trx")
        text_file(directory / "coverage.cobertura.xml", "<coverage />")

        return directory

    def fake_command(self, runner):
        """Retain synthetic command logs while leaving result validation and assembly real."""
        def execute(stage, name, arguments, env=None):
            log = runner.output / "logs" / stage / f"{name}.log"
            text_file(log, "fixture command passed\n")
            runner.command_logs.append(log)

        return patch.object(runner, "command", side_effect=execute)

class RestoredIdentityTests(ParallelFixture):
    """Every consumer must retain the producer's source, version, attempt, and UTC deadline."""

    def test_matching_identity_is_restored_without_rewriting_it(self):
        """A valid restored job uses the exact producer identity bytes."""
        # Arrange
        original = (self.runner.output / "identity.json").read_bytes()

        # Act
        restored = self.restored()

        # Assert
        self.assertEqual(self.runner.identity, restored.identity)
        self.assertEqual(original, (self.runner.output / "identity.json").read_bytes())

    def test_wrong_commit_version_attempt_or_deadline_is_rejected(self):
        """Similar artifacts from another producer cannot join the current attempt."""
        # Arrange
        different = copy.deepcopy(self.source)
        different["commit"] = "d" * 40
        cases = (
            (patch.object(qualification, "source_identity", return_value=different), {}, different["commit"]),
            (patch.dict(os.environ, {"GITHUB_RUN_ATTEMPT": "2"}), {}, COMMIT),
            (patch.dict(os.environ, {}), {"version": "0.1.0-rc.2"}, COMMIT),
            (patch.dict(os.environ, {}), {"deadline_utc": self.runner.deadline_utc + 1}, COMMIT),
        )

        for context, arguments, commit in cases:
            with self.subTest(arguments=arguments, commit=commit), context, \
                    patch.dict(os.environ, {"GITHUB_SHA": commit}):
                # Act
                error = capture(lambda: self.restored(**arguments))

                # Assert
                self.assertIsInstance(error, common.ReleaseError)
                self.assertEqual("build_identity", error.code)

    def test_restored_timeout_uses_remaining_shared_utc_budget(self):
        """A fresh consumer process must not receive a fresh two-hour deadline."""
        # Arrange
        identity = common.read_json(self.runner.output / "identity.json")
        identity["deadlineUtc"] = 1500
        common.write_json(self.runner.output / "identity.json", identity)

        # Act
        with patch.object(qualification.time, "time", return_value=1200), \
                patch.object(qualification.time, "monotonic", return_value=30):
            restored = self.restored(timeout_seconds=7200)

        # Assert
        self.assertEqual(1500, restored.deadline_utc)
        self.assertEqual(330, restored.deadline)

    def test_expired_shared_deadline_is_rejected_before_job_execution(self):
        """Downloading a valid artifact does not revive an expired qualification attempt."""
        # Arrange
        identity = common.read_json(self.runner.output / "identity.json")
        identity["deadlineUtc"] = int(time.time()) - 1
        common.write_json(self.runner.output / "identity.json", identity)

        # Act
        error = capture(self.restored)

        # Assert
        self.assertEqual("deadline", error.code)


class PrebuiltJobTests(ParallelFixture):
    """Consumers execute the transferred build and reject mutation before exporting results."""

    def test_executable_inventory_excludes_the_specification_library(self):
        """Shared test sources do not create another executable job."""
        # Act
        projects = qualification.test_projects(self.repo)

        # Assert
        self.assertEqual(set(PROJECTS), {path.stem for path in projects})

    def test_unknown_fixed_job_is_rejected_before_output_export(self):
        """An unrecognized job name cannot produce an empty success artifact."""
        # Act
        error = capture(lambda: self.runner.run_job("unexpected"))

        # Assert
        self.assertEqual("job", error.code)
        self.assertFalse((self.runner.output / "evidence").exists())

    def test_test_job_targets_the_dll_and_transported_adapter_and_collector(self):
        """A test consumer runs VSTest against producer bytes without MSBuild evaluation."""
        # Arrange
        self.report(self.runner, "First.Tests")
        binary = self.runner.output / "build/bin/First.Tests/release/First.Tests.dll"

        # Act
        with patch.object(self.runner, "command") as command:
            role, directory = self.runner.test_project("tests/First.Tests/First.Tests.csproj")

        # Assert
        arguments = command.call_args.args[2]
        self.assertEqual(["dotnet", "test", str(binary)], arguments[:3])
        self.assertEqual(f"{binary.parent};{self.collector}", arguments[arguments.index("--test-adapter-path") + 1])
        self.assertIn("--collect:Code Coverage", arguments)
        self.assertFalse(any(value.endswith(".csproj") or value.startswith("-p:") for value in arguments))
        self.assertEqual("test-First.Tests", role)
        self.assertEqual(self.runner.output / "tests/First.Tests", directory)
        self.assertEqual(str(directory / "query-plans"), command.call_args.args[3]["DOKA_NESTEDSET_QUERY_PLAN_DIR"])

    def test_sample_job_targets_the_prebuilt_dll(self):
        """A fresh sample runner consumes the build directly and records its own SQLite success."""
        # Arrange
        binary = self.runner.output / "build/bin/FileSystem/release/FileSystem.dll"

        # Act
        with patch.object(self.runner, "command") as command:
            role, directory = self.runner.sample("FileSystem")

        # Assert
        self.assertEqual(["dotnet", str(binary), "--provider", "sqlite", "--reset"], command.call_args.args[2])
        self.assertEqual("sample-FileSystem", role)
        self.assertEqual({"sample": "FileSystem", "passed": True}, common.read_json(directory / "result.json"))

    def test_missing_collector_rejects_prebuilt_test_before_execution(self):
        """An adapter in the DLL directory cannot replace the restored coverage collector."""
        # Arrange
        (self.collector / "Microsoft.VisualStudio.TraceDataCollector.dll").unlink()

        # Act
        with patch.object(self.runner, "command") as command:
            error = capture(lambda: self.runner.test_project("tests/First.Tests/First.Tests.csproj"))

        # Assert
        self.assertEqual("test_binary", error.code)
        command.assert_not_called()

    def test_build_transports_the_complete_collector_tree(self):
        """Native coverage engines and nested support files remain available on fresh test runners."""
        # Arrange
        original = common.file_inventory(self.collector)
        source = self.root / "nuget/collector"
        shutil.copytree(self.collector, source)
        shutil.rmtree(self.collector)

        def execute(stage, name, arguments, env=None):
            if name == "collector-path":
                text_file(self.runner.output / "logs/quality/collector-path.log", str(source) + "\n")

        # Act
        with patch.object(self.runner, "command", side_effect=execute):
            self.runner.compile()

        # Assert
        self.assertEqual(original, common.file_inventory(self.collector))
        runtime = common.read_json(self.runner.output / "quality/runtime.json")
        self.assertIn(".coverage-collector/runtimes/linux-x64/native/libcoverage.so", runtime)

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

    def test_package_mutation_after_consumer_cannot_export_results(self):
        """The post-job integrity boundary catches consumer commands that overwrite package input."""
        # Arrange
        path = self.runner.output / "packages" / f"{common.PACKAGES[0]}.{VERSION}.nupkg"

        # Act
        with patch.object(self.runner, "consumer", side_effect=lambda: text_file(path, "changed by consumer")):
            error = capture(lambda: self.runner.run_job("consumer"))

        # Assert
        self.assertEqual("package_inputs", error.code)
        self.assertFalse((self.runner.output / "evidence").exists())

    def test_runtime_mutation_after_sample_cannot_export_results(self):
        """A consumer cannot rebuild an execution assembly and still claim producer provenance."""
        # Arrange
        binary = self.runner.output / "build/bin/FileSystem/release/FileSystem.dll"

        # Act
        with patch.object(self.runner, "command", side_effect=lambda *_: text_file(binary, "rebuilt sample")):
            error = capture(lambda: self.runner.run_job("sample", sample="FileSystem"))

        # Assert
        self.assertEqual("runtime_changed", error.code)
        self.assertFalse((self.runner.output / "evidence").exists())


class AggregationTests(ParallelFixture):
    """Merged native artifacts must contain every required executable result before sealing."""

    def setUp(self):
        """Populate ordinary merged test and sample outputs without an artifact catalog."""
        super().setUp()
        for project in PROJECTS:
            self.report(self.runner, project)

        plans = self.runner.output / "tests/First.Tests/query-plans"
        for engine in ("MySql", "MariaDb", "PostgreSql", "SqlServer", "Sqlite"):
            text_file(plans / f"{engine}-key-filter.txt", "key filter")
            if engine != "Sqlite":
                text_file(plans / f"{engine}-hierarchy.txt", "hierarchy")

        for sample in SAMPLES:
            common.write_json(self.runner.output / "tests/samples" / sample / "result.json",
                              {"sample": sample, "passed": True})

        packages = common.read_json(self.runner.output / "packages/package-manifest.json")["packages"]
        common.write_json(self.runner.output / "consumer/result.json",
                          {"success": True, "version": VERSION, "consumers": [{"name": "core"}, {"name": "ef"}],
                           "candidateSha256": {record["id"]: record["nupkgSha256"] for record in packages}})

    def aggregate_error(self):
        """Exercise real result verification and assembly; mock external coverage/SBOM commands only."""
        with self.fake_command(self.runner), redirect_stdout(io.StringIO()):
            return capture(self.runner.aggregate)

    def test_complete_merged_results_seal_existing_packages(self):
        """Native transport needs no per-job receipt or independent role schema."""
        # Arrange
        package = self.runner.output / "packages" / f"{common.PACKAGES[0]}.{VERSION}.nupkg"
        digest = common.sha256(package)

        # Act
        error = self.aggregate_error()

        # Assert
        self.assertIsNone(error)
        candidate = self.runner.output / "candidate"
        self.assertEqual(COMMIT, common.load_candidate(candidate)["source"]["commit"])
        self.assertEqual(digest, common.sha256(candidate / package.name))
        with zipfile.ZipFile(candidate / "qualification-evidence.zip") as archive:
            self.assertIn("tests/samples/UserGroups/result.json", archive.namelist())
            self.assertFalse(any(name.endswith("receipt.json") for name in archive.namelist()))

    def test_missing_project_coverage_or_plan_rejects_partial_artifacts(self):
        """A native pattern may match zero or fewer artifacts; complete result verification must reject it."""
        # Arrange
        cases = (("tests/Second.Tests/Second.Tests.trx", "tests_missing"),
                 ("tests/Second.Tests/coverage.cobertura.xml", "coverage_missing"),
                 ("tests/First.Tests/query-plans/MariaDb-hierarchy.txt", "query_plans_missing"))
        for relative, code in cases:
            path = self.runner.output / relative
            original = path.read_bytes()
            path.unlink()
            with self.subTest(relative=relative):
                # Act
                error = self.aggregate_error()

                # Assert
                self.assertEqual(code, error.code)
                self.assertFalse((self.runner.output / "candidate").exists())

            path.write_bytes(original)
            shutil.rmtree(self.runner.output / "tests/query-plans", ignore_errors=True)

    def test_missing_or_failed_sample_rejects_partial_artifacts(self):
        """A passing matrix cannot replace its promised retained sample result."""
        # Arrange
        path = self.runner.output / "tests/samples/Kpis/result.json"
        cases = (None, {"sample": "Kpis", "passed": False})
        for value in cases:
            if value is None:
                path.unlink()
            else:
                common.write_json(path, value)

            with self.subTest(value=value):
                # Act
                error = self.aggregate_error()

                # Assert
                self.assertEqual("invalid_json" if value is None else "sample_result", error.code)
                self.assertFalse((self.runner.output / "candidate").exists())

            shutil.rmtree(self.runner.output / "tests/query-plans", ignore_errors=True)

    def test_false_wrong_version_or_incomplete_consumer_is_rejected(self):
        """Existing consumer success must name both applications and the selected version."""
        # Arrange
        path = self.runner.output / "consumer/result.json"
        original = common.read_json(path)
        cases = ({"success": False}, {"version": "0.1.0-rc.2"}, {"consumers": [{"name": "core"}]})
        for change in cases:
            common.write_json(path, {**original, **change})
            with self.subTest(change=change):
                # Act
                error = self.aggregate_error()

                # Assert
                self.assertEqual("consumer_result", error.code)
                self.assertFalse((self.runner.output / "candidate").exists())

            shutil.rmtree(self.runner.output / "tests/query-plans", ignore_errors=True)

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
            shutil.rmtree(self.runner.output / "tests/query-plans", ignore_errors=True)
            changed = {**original, "candidateSha256": value}
            if value is None:
                changed.pop("candidateSha256")

            common.write_json(path, changed)
            with self.subTest(hashes=value):
                # Act
                error = self.aggregate_error()

                # Assert
                self.assertIsInstance(error, common.ReleaseError)
                self.assertEqual("consumer_packages", error.code)
                self.assertFalse((self.runner.output / "candidate").exists())

    def test_offline_sbom_rejection_prevents_sealing(self):
        """Aggregation reuses the existing offline verifier and honors its failed process result."""
        # Arrange
        commands = []
        def execute(stage, name, arguments, env=None):
            commands.append(arguments)
            if name == "aggregate-verify":
                raise common.ReleaseError("command_failed", "invalid retained SBOM")

        # Act
        with patch.object(self.runner, "command", side_effect=execute):
            error = capture(self.runner.aggregate)

        # Assert
        self.assertEqual("command_failed", error.code)
        self.assertIn("eng/verify-package-sbom.py", commands[-1])
        self.assertIn("verify", commands[-1])
        self.assertFalse((self.runner.output / "candidate").exists())

    def test_export_retains_hidden_results_and_only_owned_logs(self):
        """Ordinary output export includes dot-files without copying producer logs or build identities."""
        # Arrange
        results = self.runner.output / "tests/First.Tests"
        text_file(results / ".vstest-state", "hidden result")
        owned = self.runner.output / "logs/tests/First.Tests.log"
        text_file(owned, "this test job")
        text_file(self.runner.output / "logs/quality/build.log", "imported build log")
        self.runner.command_logs.append(owned)

        # Act
        self.runner.export_job_outputs("test-First.Tests", results)

        # Assert
        evidence = self.runner.output / "evidence"
        self.assertEqual("hidden result", (evidence / "tests/First.Tests/.vstest-state").read_text())
        self.assertEqual("this test job", (evidence / "logs/test-First.Tests/tests/First.Tests.log").read_text())
        self.assertFalse((evidence / "identity.json").exists())
        self.assertFalse(any(path.name == "build.log" for path in evidence.rglob("*")))


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

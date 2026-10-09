"""Run one complete qualification locally, in CI, or for an RC."""

import argparse
import copy
import hashlib
import json
import os
import re
import shutil
import signal
import subprocess
import sys
import threading
import time
import xml.etree.ElementTree as ET
import zipfile
from contextlib import suppress
from pathlib import Path

from eng.release.common import (
    PACKAGES,
    RELEASE_WORKFLOW,
    REPOSITORY,
    STAGES,
    ReleaseError,
    file_inventory,
    load_candidate,
    read_json,
    release_version,
    require,
    reviewed_release_notes,
    sha256,
    write_json,
)

ROOT = Path(__file__).resolve().parents[2]
SOLUTION = "Doka.EntityFrameworkCore.NestedSet.slnx"
TRX = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def git(repo, *arguments, required=True):
    """Inspect Git without staging, fetching, or modifying repository state."""
    # WHY: Git status can otherwise refresh the index stat cache during an intended read-only inspection.
    result = subprocess.run(["git", *arguments], cwd=repo, capture_output=True, timeout=30,
                            env={**os.environ, "GIT_OPTIONAL_LOCKS": "0"}, check=False)
    require(not required or result.returncode == 0, "git_inspection", "Cannot inspect repository identity.")

    return result.stdout.decode().strip() if result.returncode == 0 else None


def source_identity(repo, workspace):
    """Bind current file bytes as well as the Git identity; workspace runs never become releases."""
    files = git(repo, "ls-files", "--cached", "--others", "--exclude-standard", "-z").split("\0")
    inputs = {}
    for name in sorted(set(files)):
        if not name:
            continue

        path = repo / name
        if not path.exists() and not path.is_symlink():
            # WHY: A staged source move can leave deleted index entries during workspace verification.
            inputs[name] = None
            continue

        require(path.is_file() and not path.is_symlink(), "source_entry", f"Missing or linked source: {name}.")
        inputs[name] = sha256(path)

    commit = git(repo, "rev-parse", "--verify", "HEAD", required=False)
    tree = git(repo, "rev-parse", "--verify", "HEAD^{tree}", required=False)
    dirty = bool(git(repo, "status", "--porcelain", "--untracked-files=all"))
    require(workspace or (commit and not dirty), "unreviewed_source",
            "Qualification requires a clean committed source tree.",
            "Use --workspace for explicitly non-publishable local verification, or review and commit separately.")
    fingerprint = hashlib.sha256(json.dumps(inputs, sort_keys=True).encode("ascii")).hexdigest()

    return {"commit": commit, "tree": tree, "fingerprint": fingerprint, "files": inputs, "dirty": dirty}


def solution_projects(repo):
    """Derive the project set from the solution instead of maintaining another project list."""
    paths = [repo / node.attrib["Path"] for node in ET.parse(repo / SOLUTION).findall(".//Project")]
    require(bool(paths) and len(paths) == len(set(paths)), "solution_projects", "Invalid solution project inventory.")
    require(all(path.is_file() and path.resolve().is_relative_to(repo.resolve()) for path in paths),
            "solution_projects", "A solution project is absent or escapes the repository.")

    return paths


def rebased_lock(value, version):
    """Rebase only release-dependent internal project edges, preserving all external package records."""
    result = copy.deepcopy(value)
    for framework in result["dependencies"].values():
        for dependency in framework.values():
            if dependency.get("type") != "Project":
                continue

            for name in dependency.get("dependencies", {}):
                if name in PACKAGES:
                    dependency["dependencies"][name] = f"[{version}, )"

    return result


def prepare_locks(repo, output, version):
    """Copy both shipping-package locks without requiring locks for nonshipping solution projects."""
    records = {}
    projects = set(solution_projects(repo))
    # WHY: Only the published package graphs are reviewed locks; tests, samples, and tools restore their own graphs.
    for package in PACKAGES:
        project = repo / "src" / package / f"{package}.csproj"
        require(project in projects, "package_project_missing", f"Shipping project is absent from the solution: {project}.")
        original = project.parent / "packages.lock.json"
        require(original.is_file(), "lock_missing", f"Missing reviewed lockfile: {original}.")
        target = output / "locks" / project.stem / "packages.lock.json"
        value = rebased_lock(read_json(original), version)
        write_json(target, value)
        records[project.stem] = {"source": original.relative_to(repo).as_posix(), "value": value}

    write_json(output / "quality" / "dependencies.json", records)


def verify_locks(output):
    """Require restore to preserve every prepared locked graph, including project edges."""
    for project, expected in read_json(output / "quality" / "dependencies.json").items():
        require(read_json(output / "locks" / project / "packages.lock.json") == expected["value"],
                "dependencies_changed", f"Locked dependencies changed for {project}.")


def runtime_inventory(output):
    """Record the actual execution copies, including test-local shipping assemblies and symbols."""
    return file_inventory(output / "build" / "bin")


def test_projects(repo):
    """Return executable solution tests, excluding the shared specification library."""
    return [path for path in solution_projects(repo)
            if path.relative_to(repo).parts[0] == "tests"
            and ET.parse(path).findtext(".//IsTestProject") != "false"]


def verify_query_plans(directory):
    """Require the live provider plan families that the full test suite promises."""
    engines = ("MySql", "MariaDb", "PostgreSql", "SqlServer")
    names = {f"{engine}-hierarchy.txt" for engine in engines}
    names.update(f"{engine}-key-filter.txt" for engine in (*engines, "Sqlite"))
    require(all((directory / name).is_file() and (directory / name).stat().st_size > 0 for name in names),
            "query_plans_missing", "Required provider hierarchy or key-filter plans are missing or empty.")


def verify_results(repo, directory, expected=None):
    """Reject missing projects, skipped/failed tests, or a zero-test success."""
    # WHY: Specification libraries share test sources but do not execute tests or produce TRX artifacts.
    expected = {path.stem for path in test_projects(repo)} if expected is None else set(expected)
    observed = {}
    for path in sorted(directory.rglob("*.trx")):
        tree = ET.parse(path)
        counters = tree.find(".//t:Counters", TRX)
        cases = tree.findall(".//t:UnitTestResult", TRX)
        definitions = tree.findall(".//t:UnitTest", TRX)
        require(counters is not None and cases and definitions, "tests_empty", f"Incomplete TRX: {path.name}.")
        storage = {Path(item.attrib["storage"].replace("\\", "/")).stem.casefold() for item in definitions}
        matches = {name for name in expected if name.casefold() in storage}
        require(len(matches) == 1, "tests_identity", f"TRX has ambiguous or unexpected project: {path.name}.")
        name = matches.pop()
        require(name not in observed, "tests_duplicate", f"Duplicate project result: {name}.")
        case_ids = [item.get("testId") for item in cases]
        definition_ids = [item.get("id") for item in definitions]
        require(all(case_ids) and len(case_ids) == len(set(case_ids))
                and len(definition_ids) == len(set(definition_ids)) and set(case_ids) == set(definition_ids),
                "tests_duplicate", f"Duplicate or unbound test identities: {name}.")
        count = int(counters.attrib["total"])
        require(count > 0 and len(cases) == count and int(counters.attrib["executed"]) == count
                and int(counters.attrib["passed"]) == count and int(counters.attrib["failed"]) == 0
                and all(item.attrib.get("outcome") == "Passed" for item in cases),
                "tests_not_passed", f"Every selected test must pass without skipping: {name}.")
        observed[name] = {"passed": count, "trx": str(path.relative_to(directory)), "sha256": sha256(path)}

    require(set(observed) == expected, "tests_missing", "Not every solution test project supplied a passing result.")

    return observed


def release_notes(repo, version, development):
    """Select the reviewed version's changelog section; a real RC cannot use an unreleased placeholder."""
    if not development:
        # WHY: An MSBuild version override alone does not prove that the source release line was reviewed.
        return reviewed_release_notes(repo, version)

    text = (repo / "CHANGELOG.md").read_text(encoding="ascii")
    pattern = rf"^## {re.escape(version)}(?: \([^\n]*\))?\n(.*?)(?=^## |\Z)"
    match = re.search(pattern, text, re.MULTILINE | re.DOTALL)
    require(match is not None and match.group(1).strip(), "release_notes_missing",
            f"CHANGELOG.md needs a nonempty section for {version}.",
            "Prepare and review the exact version's release notes before RC qualification.")
    return f"# {version}\n\n{match.group(1).strip()}\n"


class Qualification:
    """Qualify one source and package version in a fresh, single run."""

    def __init__(self, repo, output, version, workspace=False, timeout_seconds=7200,
                 existing=False, deadline_utc=None):
        self.repo = repo.resolve()
        self.output = output.resolve()
        self.version = release_version(version, allow_development=True)
        self.command_logs = []
        if existing:
            previous_identity = read_json(self.output / "identity.json")
            deadline_utc = previous_identity.get("deadlineUtc") if deadline_utc is None else deadline_utc

        self.deadline_utc = int(time.time() + timeout_seconds) if deadline_utc is None else int(deadline_utc)
        self.deadline = time.monotonic() + self.deadline_utc - time.time()
        require(self.deadline > time.monotonic(), "deadline", "Qualification deadline elapsed.")
        self.workspace = workspace
        # WHY: A local VSTest filter can silently select a passing subset in every project.
        require(not any(value for name, value in os.environ.items()
                        if name.casefold() in ("vstesttestcasefilter", "vstestclirunsettings")),
                "test_filter", "Remove inherited VSTest filtering or inline run settings for full qualification.")
        require(self.output != self.repo and not self.repo.is_relative_to(self.output),
                "output_scope", "Output cannot replace the source tree or an ancestor.")
        require(not self.output.is_relative_to(self.repo) or self.output.is_relative_to(self.repo / "artifacts"),
                "output_scope", "In-repository qualification output must remain under ignored artifacts/.")
        self.source = source_identity(self.repo, workspace)
        self.workflow = {"runId": os.environ.get("GITHUB_RUN_ID", ""),
                         "attempt": int(os.environ.get("GITHUB_RUN_ATTEMPT", "0")),
                         "ref": os.environ.get("GITHUB_REF", ""),
                         "workflow": RELEASE_WORKFLOW}
        hosted_release = (os.environ.get("GITHUB_ACTIONS") == "true"
                          and os.environ.get("GITHUB_EVENT_NAME") == "workflow_dispatch"
                          and os.environ.get("GITHUB_WORKFLOW_REF") == f"{REPOSITORY}/{RELEASE_WORKFLOW}@refs/heads/main")
        self.publishable = bool(hosted_release and not workspace and self.workflow["ref"] == "refs/heads/main")
        self.notes = release_notes(self.repo, version, not self.publishable)
        if self.publishable:
            require(os.environ.get("GITHUB_REPOSITORY") == REPOSITORY
                    and os.environ.get("GITHUB_SHA") == self.source["commit"],
                    "hosted_identity", "Hosted repository or checkout does not match the workflow source.")
            release_version(version)

        self.identity = {"schemaVersion": 1, "repository": REPOSITORY, "version": version,
                         "source": self.source, "workflow": self.workflow, "publishable": self.publishable,
                         "deadlineUtc": self.deadline_utc}
        if existing:
            # WHY: Reusing a downloaded build is allowed only within the exact producer run and attempt.
            require(previous_identity == self.identity, "build_identity",
                    "Downloaded build belongs to another source, version, workflow attempt, or deadline.")
        else:
            require(not self.output.exists() or not any(self.output.iterdir()),
                    "output_exists", "Output already contains evidence.", "Use a fresh output directory.")
            self.output.mkdir(parents=True, exist_ok=True)
            write_json(self.output / "identity.json", self.identity)

    def command(self, stage, name, arguments, env=None):
        """Stream output to a retained log and terminate the process group at the run deadline."""
        remaining = self.deadline - time.monotonic()
        require(remaining > 0, "deadline", "Qualification deadline elapsed.")
        log = self.output / "logs" / stage / f"{name}.log"
        log.parent.mkdir(parents=True, exist_ok=True)
        self.command_logs.append(log)
        print(f"[{stage}] {name}; log: {log}", flush=True)
        environment = os.environ.copy()
        environment["DOKA_NESTEDSET_RELEASE_ARTIFACTS"] = str(self.output / "build")
        if env:
            environment.update(env)

        with log.open("w") as stream:
            process = subprocess.Popen(arguments, cwd=self.repo, env=environment,
                                       stdout=subprocess.PIPE, stderr=subprocess.STDOUT, start_new_session=True,
                                       text=True, encoding="utf-8", errors="replace", bufsize=1)
            output_errors = []

            def stop_process_group():
                """Reap the leader and stop descendants even when the leader has already exited."""
                with suppress(ProcessLookupError):
                    os.killpg(process.pid, signal.SIGTERM)
                try:
                    process.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    pass
                finally:
                    # WHY: A terminated parent does not prove its descendants stopped writing evidence.
                    with suppress(ProcessLookupError):
                        os.killpg(process.pid, signal.SIGKILL)
                    process.wait()

            def copy_output():
                # WHY: A separate reader keeps the process deadline effective even when a command is silent.
                try:
                    with process.stdout:
                        for line in process.stdout:
                            stream.write(line)
                            stream.flush()
                            print(line, end="", flush=True)
                except Exception as error:
                    output_errors.append(error)
                    # WHY: Failed evidence output must interrupt a blocked writer instead of waiting for the deadline.
                    with suppress(ProcessLookupError):
                        os.killpg(process.pid, signal.SIGKILL)

            reader = threading.Thread(target=copy_output, daemon=True)
            reader.start()
            def interrupted(signum, frame):
                raise KeyboardInterrupt

            previous_handler = signal.signal(signal.SIGTERM, interrupted)
            try:
                code = process.wait(timeout=remaining)
            except (subprocess.TimeoutExpired, KeyboardInterrupt):
                # WHY: A timed-out database test must not leave its child runner writing after job failure.
                stop_process_group()
                raise ReleaseError("deadline", f"Stopped {name}; see {log}.") from None
            finally:
                signal.signal(signal.SIGTERM, previous_handler)
                reader.join(timeout=max(0, min(10, self.deadline - time.monotonic())))

            output_incomplete = reader.is_alive()
            if output_incomplete or output_errors:
                stop_process_group()
                reader.join(timeout=10)

            require(not output_incomplete and not reader.is_alive(), "command_output",
                    f"Output pipe did not close after {name}.")
            require(not output_errors, "command_output", f"Could not retain or stream output from {name}; see {log}.")

        if code:
            (log.parent / f"{name}.failed").write_text(str(code) + "\n", encoding="ascii")
            raise ReleaseError("command_failed", f"{name} exited {code}; see {log}.")

    def properties(self):
        """Keep versioned locks and build outputs inside this run's owned directory."""
        # WHY: PackageVersion alone renames the archive while the SDK keeps the assembly's source VersionPrefix.
        return [f"-p:Version={self.version}", f"-p:PackageVersion={self.version}", f"-p:ArtifactsPath={self.output / 'build'}",
                f"-p:NestedSetReleaseLockRoot={self.output / 'locks'}"]

    def assert_inputs(self, runtime=False):
        """Refuse changed source, dependency locks, or actual execution copies."""
        require(source_identity(self.repo, self.workspace) == self.source,
                "source_changed", "Source changed while qualification was running.")
        if (self.output / "quality" / "dependencies.json").exists():
            verify_locks(self.output)

        if runtime:
            require(runtime_inventory(self.output) == read_json(self.output / "quality" / "runtime.json"),
                    "runtime_changed", "Execution binaries changed after the qualified build.",
                    "Keep this failed evidence; start a fresh run after diagnosing instrumentation or rebuilds.")

    def quality(self):
        """Run tooling checks and a locked, version-bound full solution build."""
        self.engineering()
        self.source_quality()
        self.compile()

    def engineering(self):
        """Check existing release tooling without adding documentation or benchmark gates."""
        # WHY: CI runs these offline regressions before merge; the RC rechecks its own release tools from exact main.
        self.command("quality", "release-tooling-tests", ["python3", "-m", "unittest", "discover", "-s", "eng/tests",
                                                          "-p", "test_*.py", "-v"])
        for path in sorted((self.repo / "eng").rglob("*.sh")):
            self.command("quality", "shell-" + path.relative_to(self.repo / "eng").as_posix().replace("/", "-"),
                         ["bash", "-n", str(path)])

    def restore(self):
        """Restore the reviewed shipping locks and the complete selected solution graph."""
        prepare_locks(self.repo, self.output, self.version)
        self.command("quality", "restore", ["dotnet", "restore", SOLUTION, "--locked-mode", *self.properties()])
        verify_locks(self.output)

    def source_quality(self):
        """Run Roslyn style/import checks with the selected version and owned restore paths."""
        self.restore()
        # WHY: dotnet format reads MSBuild properties through environment variables, not build-style -p arguments.
        style_env = {"Version": self.version, "PackageVersion": self.version, "ArtifactsPath": str(self.output / "build"),
                     "NestedSetReleaseLockRoot": str(self.output / "locks")}
        self.command("quality", "style", ["dotnet", "format", SOLUTION, "style", "--severity", "warn",
                                            "--verify-no-changes", "--no-restore"], style_env)
        self.command("quality", "imports", ["dotnet", "format", SOLUTION, "style", "--diagnostics", "IDE0005",
                                              "--severity", "hidden", "--verify-no-changes", "--no-restore"], style_env)

    def compile(self):
        """Build once and retain the collector needed by precompiled tests on fresh runners."""
        self.command("quality", "build", ["dotnet", "build", SOLUTION, "-c", "Release", "--no-restore",
                                            *self.properties()])
        project = test_projects(self.repo)[0]
        self.command("quality", "collector-path", ["dotnet", "msbuild", str(project), "-nologo",
                                                    "-getProperty:TraceDataCollectorDirectoryPath", *self.properties()])
        collector = Path((self.output / "logs/quality/collector-path.log").read_text().strip())
        require(collector.is_dir() and (collector / "Microsoft.VisualStudio.TraceDataCollector.dll").is_file(),
                "collector_missing", "Restored coverage collector is absent.")
        # WHY: The adapter is copied to test outputs, but the collector and native engine stay in the NuGet cache.
        shutil.copytree(collector, self.output / "build/bin/.coverage-collector")
        write_json(self.output / "quality" / "runtime.json", runtime_inventory(self.output))

    def tests(self):
        """Execute every test project and runnable sample, retaining provider query plans."""
        directory = self.output / "tests"
        self.command("tests", "docker", ["docker", "info"])
        self.command("tests", "all-projects", ["dotnet", "test", SOLUTION, "-c", "Release", "--no-build",
                                                "--no-restore", "-m:1", *self.properties(), "--settings",
                                                "eng/coverage.runsettings", "--collect:Code Coverage", "--logger",
                                                "trx", "--logger", "console;verbosity=normal",
                                                "--results-directory", str(directory)],
                     {"DOKA_NESTEDSET_QUERY_PLAN_DIR": str(directory / "query-plans")})
        write_json(directory / "results.json", verify_results(self.repo, directory))
        verify_query_plans(directory / "query-plans")
        self.command("tests", "coverage", ["python3", "eng/verify-coverage.py", str(directory)])
        for sample in ("FileSystem", "Kpis", "UserGroups"):
            self.command("tests", "sample-" + sample.lower(), ["dotnet", "run", "--project",
                         "samples/" + sample, "-c", "Release", "--no-build", "--no-restore",
                         *self.properties(), "--", "--provider", "sqlite", "--reset"],
                         {"NESTEDSET_SAMPLE_SQLITE_DIRECTORY": str(directory / "sample-data")})

    def packages(self):
        """Pack once from tested binaries and inspect primary/symbol payloads."""
        from eng.release.packages import verify_packages, verify_symbol_rejection
        self.command("packages", "pack", ["dotnet", "pack", SOLUTION, "-c", "Release", "--no-build",
                                            "--no-restore", *self.properties(), "-o", str(self.output / "packages")])
        os.environ["DOKA_NESTEDSET_RELEASE_ARTIFACTS"] = str(self.output / "build")
        verify_packages(self.output / "packages", self.version, self.source["commit"],
                        self.output / "packages" / "package-manifest.json")
        write_json(self.output / "logs/packages/symbol-rejection.json",
                   verify_symbol_rejection(self.output / "packages", self.version))

    def consumer(self):
        """Exercise the exact two candidate packages through independent package consumers."""
        self.command("consumer", "package-consumers", ["bash", "eng/verify-package-consumer.sh", "--package-dir",
                                                        str(self.output / "packages"), "--version", self.version,
                                                        "--output", str(self.output / "consumer")])

    def sbom(self):
        """Generate and independently validate each package's actual resolved SPDX dependency closure."""
        self.command("sbom", "generate-verify", ["bash", "eng/generate-sbom.sh", "--package-dir",
                                                 str(self.output / "packages"), "--output", str(self.output / "sbom"),
                                                 "--version", self.version])

    def verify_package_inputs(self):
        """Require downloaded primary/symbol bytes to match the producer's inspected manifest."""
        records = read_json(self.output / "packages/package-manifest.json")["packages"]
        require({record["id"] for record in records} == set(PACKAGES) and len(records) == len(PACKAGES),
                "package_inputs", "Producer package inventory is incomplete or duplicated.")
        for record in records:
            require(record["version"] == self.version, "package_inputs", "Producer package version differs.")
            for extension, field in (("nupkg", "nupkgSha256"), ("snupkg", "snupkgSha256")):
                name = f"{record['id']}.{self.version}.{extension}"
                require(record[extension] == name and sha256(self.output / "packages" / name) == record[field],
                        "package_inputs", "Downloaded package bytes differ from the inspected producer.")

    def test_project(self, project):
        """Run one producer-built assembly with its transported adapter and coverage collector."""
        selected = {path.relative_to(self.repo).as_posix(): path.stem for path in test_projects(self.repo)}
        require(project in selected, "test_project", "Selected project is not an executable solution test.")
        name = selected[project]
        directory = self.output / "tests" / name
        binary = self.output / "build/bin" / name / "release" / f"{name}.dll"
        collector = self.output / "build/bin/.coverage-collector"
        require(binary.is_file() and (collector / "Microsoft.VisualStudio.TraceDataCollector.dll").is_file(),
                "test_binary", "Producer test assembly or coverage collector is absent.")
        self.command("tests", name, ["dotnet", "test", str(binary), "--test-adapter-path",
                                    f"{binary.parent};{collector}", "--settings", "eng/coverage.runsettings",
                                    "--collect:Code Coverage", "--logger", "trx", "--logger",
                                    "console;verbosity=normal", "--results-directory", str(directory)],
                     {"DOKA_NESTEDSET_QUERY_PLAN_DIR": str(directory / "query-plans")})
        write_json(directory / "results.json", verify_results(self.repo, directory, {name}))
        require(bool(list(directory.rglob("*.cobertura.xml"))), "coverage_missing",
                f"Selected project did not supply coverage: {name}.")

        return "test-" + name, directory

    def sample(self, name):
        """Execute a complete producer-built SQLite sample without MSBuild evaluation."""
        require(name in {path.parent.name for path in solution_projects(self.repo)
                         if path.relative_to(self.repo).parts[0] == "samples"},
                "sample", "Selected sample is absent from the solution.")
        directory = self.output / "tests/samples" / name
        binary = self.output / "build/bin" / name / "release" / f"{name}.dll"
        require(binary.is_file(), "sample_binary", "Producer sample assembly is absent.")
        self.command("tests", "sample-" + name, ["dotnet", str(binary), "--provider", "sqlite", "--reset"],
                     {"NESTEDSET_SAMPLE_SQLITE_DIRECTORY": str(directory / "data")})
        write_json(directory / "result.json", {"sample": name, "passed": True})

        return "sample-" + name, directory

    def export_job_outputs(self, label, directory=None):
        """Copy ordinary logs and results for GitHub's artifact transport."""
        evidence = self.output / "evidence"
        evidence.mkdir()
        for log in self.command_logs:
            target = evidence / "logs" / label / log.relative_to(self.output / "logs")
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(log, target)

        if directory is not None:
            shutil.copytree(directory, evidence / directory.relative_to(self.output))

    def aggregate(self):
        """Verify the native jobs' merged results and seal the existing candidate."""
        results = self.output / "tests"
        write_json(results / "results.json", verify_results(self.repo, results))
        for project in test_projects(self.repo):
            require(bool(list((results / project.stem).rglob("*.cobertura.xml"))), "coverage_missing",
                    f"Project coverage is absent: {project.stem}.")

        for path in results.glob("*/query-plans/*"):
            target = results / "query-plans" / path.name
            require(not target.exists(), "query_plans_duplicate", f"Duplicate provider plan: {path.name}.")
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(path, target)

        verify_query_plans(results / "query-plans")
        self.command("tests", "coverage", ["python3", "eng/verify-coverage.py", str(results)])
        for project in solution_projects(self.repo):
            if project.relative_to(self.repo).parts[0] != "samples":
                continue

            name = project.parent.name
            require(read_json(results / "samples" / name / "result.json") == {"sample": name, "passed": True},
                    "sample_result", "Sample success evidence is incomplete.")

        consumer = read_json(self.output / "consumer/result.json")
        require(consumer.get("success") is True and consumer.get("version") == self.version
                and len(consumer.get("consumers", [])) == 2
                and {item.get("name") for item in consumer["consumers"]} == {"core", "ef"},
                "consumer_result", "Both exact-version package consumers must have succeeded.")
        # WHY: Consumers use the small package artifact; sealing uses the build artifact's package copies.
        expected_hashes = {record["id"]: record["nupkgSha256"] for record in
                           read_json(self.output / "packages/package-manifest.json")["packages"]}
        require(consumer.get("candidateSha256") == expected_hashes, "consumer_packages",
                "Consumer package bytes differ from the inspected packages being sealed.")
        # WHY: Reuse the existing offline SBOM verifier; do not add a second result schema or validator.
        self.command("sbom", "aggregate-verify", ["python3", "eng/verify-package-sbom.py", "verify", "--package-dir",
                                                str(self.output / "packages"), "--output", str(self.output / "sbom"),
                                                "--version", self.version])
        self.verify_package_inputs()
        self.assemble()

    def run_job(self, job, project=None, sample=None):
        """Execute only the fixed RC job selected by GitHub; no scheduling or partial-run recovery."""
        require(job in {"source-quality", "engineering-tests", "build", "test", "sample", "consumer", "sbom",
                        "aggregate"}, "job", "Unknown RC job.")
        has_build = (self.output / "build/bin").is_dir()
        self.assert_inputs(runtime=has_build)
        if job in {"test", "sample", "consumer", "sbom", "aggregate"}:
            self.verify_package_inputs()

        label, directory = job, None
        if job == "source-quality":
            self.source_quality()
        elif job == "engineering-tests":
            self.engineering()
        elif job == "build":
            self.restore()
            self.compile()
            self.packages()
        elif job == "test":
            label, directory = self.test_project(project)
        elif job == "sample":
            label, directory = self.sample(sample)
        elif job == "consumer":
            self.consumer()
            directory = self.output / "consumer"
        elif job == "sbom":
            self.sbom()
            directory = self.output / "sbom"
        elif job == "aggregate":
            self.aggregate()

        self.assert_inputs(runtime=has_build or job == "build")
        require(time.monotonic() < self.deadline, "deadline", "Qualification deadline elapsed.")
        if job not in {"build", "aggregate"}:
            if has_build or job in {"consumer", "sbom"}:
                self.verify_package_inputs()

            self.export_job_outputs(label, directory)

    def run(self):
        """Run every check before sealing the candidate produced by this process."""
        for stage in STAGES:
            self.assert_inputs(runtime=stage != "quality")
            (self.output / stage).mkdir()
            getattr(self, stage)()
            self.assert_inputs(runtime=True)

        self.assemble()

    def assemble(self):
        """Seal a portable candidate without building, restoring, or modifying any verified artifact."""
        require(time.monotonic() < self.deadline, "deadline", "Qualification deadline elapsed before sealing.")
        self.assert_inputs(runtime=True)
        candidate = self.output / "candidate"
        require(not candidate.exists(), "candidate_exists", "Candidate output already exists.")
        candidate.mkdir()
        for package in PACKAGES:
            for extension in ("nupkg", "snupkg"):
                name = f"{package}.{self.version}.{extension}"
                shutil.copyfile(self.output / "packages" / name, candidate / name)

            manifest = self.output / "sbom" / package / "_manifest/spdx_2.2/manifest.spdx.json"
            shutil.copyfile(manifest, candidate / f"{package}.spdx.json")

        shutil.copyfile(self.output / "packages/package-manifest.json", candidate / "package-manifest.json")
        (candidate / "release-notes.md").write_text(
            self.notes, encoding="ascii")
        with zipfile.ZipFile(candidate / "qualification-evidence.zip", "w", zipfile.ZIP_DEFLATED) as archive:
            for path in self.evidence_files():
                archive.write(path, path.relative_to(self.output).as_posix())

        identity = {name: value for name, value in self.source.items() if name != "files"}
        write_json(candidate / "candidate.json",
                   {"schemaVersion": 1, "repository": REPOSITORY, "version": self.version,
                    "tag": "v" + self.version, "source": identity, "workflow": self.workflow,
                    "publishable": self.publishable, "stages": list(STAGES), "files": file_inventory(candidate)})
        load_candidate(candidate, require_publishable=self.publishable)
        summary = (f"# Qualified candidate {self.version}\n\n"
                   f"- Source commit: `{self.source['commit'] or 'uncommitted workspace'}`\n"
                   f"- Source fingerprint: `{self.source['fingerprint']}`\n"
                   f"- Publishable hosted candidate: `{str(self.publishable).lower()}`\n"
                   f"- Workflow run/producer attempt: `{self.workflow['runId']}/{self.workflow['attempt']}`\n"
                   f"- Candidate manifest SHA-256: `{sha256(candidate / 'candidate.json')}`\n"
                   "- Passed stages: " + ", ".join(STAGES) + "\n\n"
                   "Review candidate.json, package-manifest.json, qualification-evidence.zip, and the retained logs.\n"
                   "A publishable candidate still requires provenance, the signed tag, and nuget environment approval.\n")
        (self.output / "operator-summary.md").write_text(summary, encoding="ascii")
        if os.environ.get("GITHUB_STEP_SUMMARY"):
            with Path(os.environ["GITHUB_STEP_SUMMARY"]).open("a") as stream:
                stream.write(summary)

        print(summary, flush=True)

    def evidence_files(self):
        """Select portable evidence from this uninterrupted qualification."""
        files = [self.output / "identity.json"]
        for folder in (*STAGES, "logs"):
            files.extend(path for path in (self.output / folder).rglob("*")
                         if path.is_file() and path.suffix not in (".nupkg", ".snupkg"))

        return sorted(files)


def main():
    """Expose a stable operator entry point; qualification performs no external publication."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--workspace", action="store_true", help="Non-publishable verification of local changes.")
    parser.add_argument("--timeout-seconds", type=int, default=7200)
    parser.add_argument("--deadline-utc", type=int, help="Shared Unix UTC deadline established by hosted preflight.")
    parser.add_argument("--job", choices=("source-quality", "engineering-tests", "build", "test", "sample",
                                          "consumer", "sbom", "aggregate"))
    parser.add_argument("--project", help="Exact executable solution project for a test job.")
    parser.add_argument("--sample", help="Exact solution sample name.")
    args = parser.parse_args()
    try:
        require(60 <= args.timeout_seconds <= 14400, "deadline", "Timeout must be 60 through 14400 seconds.")
        existing = args.job in {"test", "sample", "consumer", "sbom", "aggregate"}
        runner = Qualification(ROOT, args.output, args.version, args.workspace, args.timeout_seconds,
                               existing=existing, deadline_utc=args.deadline_utc)
        if args.job:
            runner.run_job(args.job, args.project, args.sample)
        else:
            runner.run()

        return 0
    except (ReleaseError, OSError, ValueError, ET.ParseError) as error:
        print(f"Qualification failed: {error}", file=sys.stderr)

        return 1


if __name__ == "__main__":
    sys.exit(main())

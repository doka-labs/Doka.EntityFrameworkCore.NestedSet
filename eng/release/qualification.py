"""Build exact packages for CI and RC runs and seal hosted candidates from shared qualification evidence."""

import argparse
import copy
import hashlib
import json
import os
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
    CONSUMERS,
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
from eng.release.lines import inspect_toolchain, validate_snapshot
from eng.release.publication import parse_json

ROOT = Path(__file__).resolve().parents[2]
SOLUTION = "Doka.EntityFrameworkCore.NestedSet.slnx"
TRX = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
ALLOCATION_PROJECT = "Doka.EntityFrameworkCore.NestedSet.Unit.Tests"
PROCESS_BUDGET_SECONDS = 7200


def git(repo, *arguments, required=True):
    """Inspect Git without staging, fetching, or modifying repository state."""
    # WHY: Git status can otherwise refresh the index stat cache during an intended read-only inspection.
    result = subprocess.run(["git", *arguments], cwd=repo, capture_output=True, timeout=30,
                            env={**os.environ, "GIT_OPTIONAL_LOCKS": "0"}, check=False)
    require(not required or result.returncode == 0, "git_inspection", "Cannot inspect repository identity.")

    return result.stdout.decode().strip() if result.returncode == 0 else None


def source_identity(repo):
    """Bind current file bytes as well as the Git identity of a clean committed checkout."""
    files = git(repo, "ls-files", "--cached", "--others", "--exclude-standard", "-z").split("\0")
    inputs = {}
    for name in sorted(set(files)):
        if not name:
            continue

        path = repo / name
        if not path.exists() and not path.is_symlink():
            # WHY: A staged source move leaves a deleted index entry; the clean-source gate below rejects it.
            inputs[name] = None
            continue

        require(path.is_file() and not path.is_symlink(), "source_entry", f"Missing or linked source: {name}.")
        inputs[name] = sha256(path)

    commit = git(repo, "rev-parse", "--verify", "HEAD", required=False)
    tree = git(repo, "rev-parse", "--verify", "HEAD^{tree}", required=False)
    dirty = bool(git(repo, "status", "--porcelain", "--untracked-files=all"))
    require(commit and not dirty, "unreviewed_source", "Qualification requires a clean committed source tree.",
            "Review and commit the change before building qualified packages.")
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
    """Copy every shipping-package lock without requiring locks for nonshipping solution projects."""
    records = {}
    projects = set(solution_projects(repo))
    # WHY: Only the published package graphs are reviewed locks; tests, samples, and tools restore their own graphs.
    for package in PACKAGES:
        project = repo / "src" / package / f"{package}.csproj"
        require(project in projects, "package_project_missing",
                f"Shipping project is absent from the solution: {project}.")
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


def test_projects(repo):
    """Return executable solution tests, excluding the shared specification library."""
    return [path for path in solution_projects(repo)
            if path.relative_to(repo).parts[0] == "tests"
            and ET.parse(path).findtext(".//IsTestProject") != "false"]


def verify_query_plans(directory):
    """Require every promised live provider plan family from at least one test cell."""
    engines = ("MySql", "MariaDb", "PostgreSql", "SqlServer")
    names = {f"{engine}-hierarchy.txt" for engine in engines}
    names.update(f"{engine}-key-filter.txt" for engine in (*engines, "Sqlite"))
    # WHY: Cells that partition one provider project may write the same plan name; each cell keeps its own copy.
    require(all(any(path.is_file() and path.stat().st_size > 0 for path in directory.glob(f"*/query-plans/{name}"))
                for name in names),
            "query_plans_missing", "Required provider hierarchy or key-filter plans are missing or empty.")


def verify_results(repo, directory, expected=None):
    """Reject missing projects, skipped/failed tests, repeated cases, or a zero-test success."""
    # WHY: Specification libraries share test sources but do not execute tests or produce TRX artifacts.
    expected = {path.stem for path in test_projects(repo)} if expected is None else set(expected)
    observed = {}
    identities = {}
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
        if name == ALLOCATION_PROJECT:
            # WHY: Instrumentation can change allocations; Unit keeps its fixed uninstrumented and covered partitions.
            require(path.name in {"allocations.trx", "coverage.trx"}, "tests_partitions",
                    "Unit results must use the fixed allocation and coverage partition names.")

        # WHY: Complementary filters may split one project across cells; a repeated case would inflate the total.
        previous = identities.setdefault(name, set())
        require(previous.isdisjoint(case_ids), "tests_duplicate", f"Result partitions repeat test identities: {name}.")
        previous.update(case_ids)
        group = observed.setdefault(name, {"passed": 0, "reports": []})
        group["passed"] += count
        group["reports"].append({"passed": count, "trx": str(path.relative_to(directory)), "sha256": sha256(path)})

    require(set(observed) == expected, "tests_missing", "Not every solution test project supplied a passing result.")
    if ALLOCATION_PROJECT in expected:
        require(sorted(Path(report["trx"]).name for report in observed[ALLOCATION_PROJECT]["reports"])
                == ["allocations.trx", "coverage.trx"], "tests_partitions",
                "Both uninstrumented allocation budgets and instrumented Unit semantics must pass.")

    return observed


class Qualification:
    """Build one exact package set, or seal those packages with the shared workflow's evidence."""

    def __init__(self, repo, output, version, existing=False):
        self.repo = repo.resolve()
        self.output = output.resolve()
        self.version = release_version(version, allow_development=True)
        if existing:
            previous_identity = read_json(self.output / "identity.json")

        # WHY: Owned processes stop within one bounded budget even if GitHub only cancels the job.
        self.deadline = time.monotonic() + PROCESS_BUDGET_SECONDS
        require(self.output != self.repo and not self.repo.is_relative_to(self.output),
                "output_scope", "Output cannot replace the source tree or an ancestor.")
        require(not self.output.is_relative_to(self.repo) or self.output.is_relative_to(self.repo / "artifacts"),
                "output_scope", "In-repository qualification output must remain under ignored artifacts/.")
        self.source = source_identity(self.repo)
        self.workflow = {"runId": os.environ.get("GITHUB_RUN_ID", ""),
                         "attempt": int(os.environ.get("GITHUB_RUN_ATTEMPT", "0")),
                         "ref": os.environ.get("GITHUB_REF", ""),
                         "workflow": RELEASE_WORKFLOW}
        # WHY: A called workflow reports its caller here, so only the release workflow's jobs bind a release line.
        hosted_release = (os.environ.get("GITHUB_ACTIONS") == "true"
                          and os.environ.get("GITHUB_EVENT_NAME") == "workflow_dispatch"
                          and os.environ.get("GITHUB_WORKFLOW_REF") ==
                          f"{REPOSITORY}/{RELEASE_WORKFLOW}@{self.workflow['ref']}")
        self.line = None
        self.toolchain = None
        raw_line = os.environ.get("NESTEDSET_RELEASE_LINE", "")
        if hosted_release or raw_line:
            require(hosted_release and bool(raw_line), "line_identity",
                    "Hosted qualification requires its independently authorized preflight line.")
            self.line = validate_snapshot(parse_json(raw_line, "frozen qualification line"),
                                          source_ref=self.workflow["ref"], version=version)
            self.toolchain = inspect_toolchain(self.repo, self.line)
            require(os.environ.get("GITHUB_REPOSITORY") == REPOSITORY
                    and os.environ.get("GITHUB_SHA") == self.source["commit"]
                    and self.workflow["runId"].isdigit() and int(self.workflow["runId"]) > 0
                    and self.workflow["attempt"] > 0,
                    "hosted_identity", "Hosted repository or checkout does not match the workflow source.")
            release_version(version)

        self.publishable = bool(self.line and self.line["state"] == "active" and not self.line["qualificationOnly"])
        self.identity = {"schemaVersion": 2 if self.line else 1, "repository": REPOSITORY, "version": version,
                         "source": self.source, "workflow": self.workflow, "publishable": self.publishable}
        if self.line:
            # WHY: Reuse the existing exact-attempt identity from build to seal; no second
            # receipt/inventory protocol is needed for the frozen branch grant or its own toolchain.
            self.identity.update(line=self.line, toolchain=self.toolchain)
        if existing:
            # WHY: Sealing downloaded packages is allowed only within the exact producer run and attempt.
            require(previous_identity == self.identity, "build_identity",
                    "Downloaded packages belong to another source, version, or workflow attempt.")
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
                # WHY: A timed-out command must not leave its child process writing after job failure.
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
        return [f"-p:Version={self.version}", f"-p:PackageVersion={self.version}",
                f"-p:ArtifactsPath={self.output / 'build'}", f"-p:NestedSetReleaseLockRoot={self.output / 'locks'}"]

    def assert_inputs(self):
        """Refuse changed source or dependency locks."""
        require(source_identity(self.repo) == self.source,
                "source_changed", "Source changed while qualification was running.")
        if (self.output / "quality" / "dependencies.json").exists():
            verify_locks(self.output)

    def restore(self):
        """Restore the reviewed shipping locks and the complete selected solution graph."""
        prepare_locks(self.repo, self.output, self.version)
        self.command("quality", "restore", ["dotnet", "restore", SOLUTION, "--locked-mode", *self.properties()])
        verify_locks(self.output)

    def compile(self):
        """Build the complete Release solution once with the selected version and owned locks."""
        self.command("quality", "build", ["dotnet", "build", SOLUTION, "-c", "Release", "--no-restore",
                                            *self.properties()])

    def packages(self):
        """Pack once from the canonical build and inspect primary/symbol payloads."""
        from eng.release.packages import verify_packages, verify_symbol_rejection
        self.command("packages", "pack", ["dotnet", "pack", SOLUTION, "-c", "Release", "--no-build",
                                            "--no-restore", *self.properties(), "-o", str(self.output / "packages")])
        os.environ["DOKA_NESTEDSET_RELEASE_ARTIFACTS"] = str(self.output / "build")
        verify_packages(self.output / "packages", self.version, self.source["commit"],
                        self.output / "packages" / "package-manifest.json")
        write_json(self.output / "logs/packages/symbol-rejection.json",
                   verify_symbol_rejection(self.output / "packages", self.version))

    def build(self):
        """Produce the one inspected package set that consumers, SBOMs, and the seal verify."""
        self.restore()
        self.compile()
        self.packages()
        self.assert_inputs()

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

    def seal(self):
        """Verify the shared workflow's evidence for these exact packages and seal the candidate."""
        require(self.line is not None, "line_identity", "Only the hosted release workflow can seal a candidate.")
        self.assert_inputs()
        self.verify_package_inputs()
        results = self.output / "tests"
        write_json(results / "results.json", verify_results(self.repo, results))
        verify_query_plans(results)
        self.command("tests", "coverage", ["python3", "eng/verify-coverage.py", str(results)])
        consumer = read_json(self.output / "consumer/result.json")
        require(consumer.get("success") is True and consumer.get("version") == self.version
                and len(consumer.get("consumers", [])) == len(CONSUMERS)
                and {item.get("name") for item in consumer["consumers"]} == {name for name, _ in CONSUMERS}
                and all(item.get("success") is True for item in consumer["consumers"]),
                "consumer_result", "All exact-version package consumers must have succeeded.")
        # WHY: Consumers ran in another job; the sealed bytes must be the archives they restored.
        expected_hashes = {record["id"]: record["nupkgSha256"] for record in
                           read_json(self.output / "packages/package-manifest.json")["packages"]}
        require(consumer.get("candidateSha256") == expected_hashes, "consumer_packages",
                "Consumer package bytes differ from the inspected packages being sealed.")
        # WHY: Reuse the existing offline SBOM verifier; do not add a second result schema or validator.
        self.command("sbom", "verify", ["python3", "eng/verify-package-sbom.py", "verify", "--package-dir",
                                        str(self.output / "packages"), "--output", str(self.output / "sbom"),
                                        "--version", self.version])
        self.assemble()

    def assemble(self):
        """Seal a portable candidate without building, restoring, or modifying any verified artifact."""
        self.assert_inputs()
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
        (candidate / "release-notes.md").write_text(reviewed_release_notes(self.repo, self.version), encoding="ascii")
        with zipfile.ZipFile(candidate / "qualification-evidence.zip", "w", zipfile.ZIP_DEFLATED) as archive:
            for path in self.evidence_files():
                archive.write(path, path.relative_to(self.output).as_posix())

        identity = {name: value for name, value in self.source.items() if name != "files"}
        manifest = {"schemaVersion": 2 if self.line else 1, "repository": REPOSITORY, "version": self.version,
                    "tag": "v" + self.version, "source": identity, "workflow": self.workflow,
                    "publishable": self.publishable, "stages": list(STAGES), "files": file_inventory(candidate)}
        if self.line:
            manifest.update(line=self.line, toolchain=self.toolchain)

        write_json(candidate / "candidate.json", manifest)
        load_candidate(candidate, require_publishable=self.publishable)
        summary = (f"# Qualified candidate {self.version}\n\n"
                   f"- Source commit: `{self.source['commit']}`\n"
                   f"- Source fingerprint: `{self.source['fingerprint']}`\n"
                   f"- Publishable hosted candidate: `{str(self.publishable).lower()}`\n"
                   f"- Workflow run/producer attempt: `{self.workflow['runId']}/{self.workflow['attempt']}`\n"
                   f"- Candidate manifest SHA-256: `{sha256(candidate / 'candidate.json')}`\n"
                   "- Passed stages: " + ", ".join(STAGES) + "\n\n"
                   "Review candidate.json, package-manifest.json, qualification-evidence.zip, and the retained logs.\n"
                   "A publishable candidate still requires provenance, the signed tag, "
                   "and nuget environment approval.\n")
        (self.output / "operator-summary.md").write_text(summary, encoding="ascii")
        if os.environ.get("GITHUB_STEP_SUMMARY"):
            with Path(os.environ["GITHUB_STEP_SUMMARY"]).open("a") as stream:
                stream.write(summary)

        print(summary, flush=True)

    def evidence_files(self):
        """Select portable evidence from this exact build and its sealed qualification."""
        files = [self.output / "identity.json"]
        for folder in (*STAGES, "logs"):
            files.extend(path for path in (self.output / folder).rglob("*")
                         if path.is_file() and path.suffix not in (".nupkg", ".snupkg"))

        return sorted(files)


def main():
    """Expose the shared build, the hosted seal, and read-only result verification."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--job", choices=("build", "seal"), help="Build exact packages or seal a hosted candidate.")
    parser.add_argument("--version")
    parser.add_argument("--output", type=Path)
    parser.add_argument("--verify-results", type=Path,
                        help="Read-only verification of passing TRX results; without --project also provider plans.")
    parser.add_argument("--project", help="Executable test assembly name for --verify-results.")
    args = parser.parse_args()
    try:
        if args.verify_results is not None:
            if any(value is not None for value in (args.job, args.version, args.output)):
                parser.error("Qualification options are incompatible with --verify-results.")

            # WHY: CI consumes existing TRX files without source/release binding or output initialization.
            projects = {path.stem for path in test_projects(ROOT)}
            require(args.project is None or args.project in projects, "tests_identity",
                    "Selected assembly is not an executable solution test project.")
            results = verify_results(ROOT, args.verify_results, {args.project} if args.project else projects)
            if args.project is None:
                verify_query_plans(args.verify_results)

            print(json.dumps(results, sort_keys=True))

            return 0

        if args.job is None or args.version is None or args.output is None or args.project is not None:
            parser.error("--job, --version and --output are required; --project only selects --verify-results.")

        runner = Qualification(ROOT, args.output, args.version, existing=args.job == "seal")
        if args.job == "build":
            runner.build()
        else:
            runner.seal()

        return 0
    except (ReleaseError, OSError, ValueError, ET.ParseError) as error:
        print(f"Qualification failed: {error}", file=sys.stderr)

        return 1


if __name__ == "__main__":
    sys.exit(main())

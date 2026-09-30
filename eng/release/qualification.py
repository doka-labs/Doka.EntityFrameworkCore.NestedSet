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
    """Copy reviewed locks and adapt internal package edges without allowing dependency reevaluation."""
    records = {}
    for project in solution_projects(repo):
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


def verify_query_plans(directory):
    """Require the live provider plan families that the full test suite promises."""
    engines = ("MySql", "MariaDb", "PostgreSql", "SqlServer")
    names = {f"{engine}-hierarchy.txt" for engine in engines}
    names.update(f"{engine}-key-filter.txt" for engine in (*engines, "Sqlite"))
    require(all((directory / name).is_file() and (directory / name).stat().st_size > 0 for name in names),
            "query_plans_missing", "Required provider hierarchy or key-filter plans are missing or empty.")


def verify_results(repo, directory):
    """Reject missing projects, skipped/failed tests, or a zero-test success."""
    expected = {path.stem for path in solution_projects(repo) if path.relative_to(repo).parts[0] == "tests"}
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

    def __init__(self, repo, output, version, workspace=False, timeout_seconds=7200):
        self.repo = repo.resolve()
        self.output = output.resolve()
        self.version = release_version(version, allow_development=True)
        self.deadline = time.monotonic() + timeout_seconds
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

        identity = {"schemaVersion": 1, "repository": REPOSITORY, "version": version,
                    "source": self.source, "workflow": self.workflow, "publishable": self.publishable}
        require(not self.output.exists() or not any(self.output.iterdir()),
                "output_exists", "Output already contains evidence.", "Use a fresh output directory.")
        self.output.mkdir(parents=True, exist_ok=True)
        write_json(self.output / "identity.json", identity)

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
                                       stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
            def interrupted(signum, frame):
                raise KeyboardInterrupt

            previous_handler = signal.signal(signal.SIGTERM, interrupted)
            try:
                code = process.wait(timeout=remaining)
            except (subprocess.TimeoutExpired, KeyboardInterrupt):
                # WHY: A timed-out database test must not leave its child runner writing after a failed receipt.
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

                raise ReleaseError("deadline", f"Stopped {name}; see {log}.") from None
            finally:
                signal.signal(signal.SIGTERM, previous_handler)

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
        prepare_locks(self.repo, self.output, self.version)
        # WHY: CI runs these offline regressions before merge; the RC rechecks its own release tools from exact main.
        self.command("quality", "release-tooling-tests", ["python3", "-m", "unittest", "discover", "-s", "eng/tests",
                                                          "-p", "test_*.py", "-v"])
        for path in sorted((self.repo / "eng").rglob("*.sh")):
            self.command("quality", "shell-" + path.relative_to(self.repo / "eng").as_posix().replace("/", "-"),
                         ["bash", "-n", str(path)])

        self.command("quality", "restore", ["dotnet", "restore", SOLUTION, "--locked-mode", *self.properties()])
        verify_locks(self.output)
        # WHY: dotnet format reads MSBuild properties through environment variables, not build-style -p arguments.
        style_env = {"Version": self.version, "PackageVersion": self.version, "ArtifactsPath": str(self.output / "build"),
                     "NestedSetReleaseLockRoot": str(self.output / "locks")}
        self.command("quality", "style", ["dotnet", "format", SOLUTION, "style", "--severity", "warn",
                                            "--verify-no-changes", "--no-restore"], style_env)
        self.command("quality", "imports", ["dotnet", "format", SOLUTION, "style", "--diagnostics", "IDE0005",
                                              "--severity", "hidden", "--verify-no-changes", "--no-restore"], style_env)
        self.command("quality", "build", ["dotnet", "build", SOLUTION, "-c", "Release", "--no-restore",
                                            *self.properties()])
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
    args = parser.parse_args()
    try:
        require(60 <= args.timeout_seconds <= 14400, "deadline", "Timeout must be 60 through 14400 seconds.")
        runner = Qualification(ROOT, args.output, args.version, args.workspace, args.timeout_seconds)
        runner.run()

        return 0
    except (ReleaseError, OSError, ValueError, ET.ParseError) as error:
        print(f"Qualification failed: {error}", file=sys.stderr)

        return 1


if __name__ == "__main__":
    sys.exit(main())

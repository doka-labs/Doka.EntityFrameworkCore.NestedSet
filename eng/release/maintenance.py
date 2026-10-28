"""Service active release lines through their own existing CI and dependency graphs."""

import argparse
import base64
import concurrent.futures
import hashlib
import os
import re
import subprocess
import sys
import threading
import time
from pathlib import Path
from urllib.parse import quote

from eng.release import publication
from eng.release.common import REPOSITORY, ReleaseError, read_json, require, write_json
from eng.release.qualification import SOLUTION, solution_projects

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = "release-line-maintenance.yml"
ECOSYSTEMS = ("dotnet-sdk", "docker", "nuget", "github-actions")
FRAMEWORK_PACKAGES = ("Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore.*",
                      "Doka.EntityFrameworkCore.MySql", "Doka.EntityFrameworkCore.SafeMigrations",
                      "Doka.EntityFrameworkCore.SafeMigrations.*", "Npgsql.EntityFrameworkCore.PostgreSQL",
                      "Microsoft.Extensions.*")
CORRELATION = re.compile(r"schedule-[1-9][0-9]{0,17}-[1-9][0-9]{0,8}\Z")
SHA = re.compile(r"[0-9a-f]{40}\Z")


def positive_number(value):
    """Reject booleans, missing IDs and unbounded GitHub numeric identities."""
    require(type(value) is int and 0 < value < 10 ** 18, "maintenance-identity", "Invalid GitHub identity.")

    return value


def active_lines(policy):
    """Include main once when active and omit registered preparation-only lines."""
    from eng.release.lines import validate_policy

    return [line for line in validate_policy(policy)["lines"] if line["state"] == "active"]


def validate_child(policy, workflow, environment):
    """Gate maintenance intent without restricting ordinary feature/manual CI."""
    requested = environment.get("INPUT_MAINTENANCE", "false")
    if requested == "false":
        return False

    require(requested == "true" and policy is not None, "maintenance-intent", "Expected explicit maintenance intent.")
    ref = environment.get("GITHUB_REF", "")
    commit = environment.get("INPUT_SOURCE_SHA", "")
    require(workflow in ("ci.yml", WORKFLOW) and environment.get("GITHUB_EVENT_NAME") == "workflow_dispatch"
            and environment.get("GITHUB_REPOSITORY") == REPOSITORY
            and SHA.fullmatch(commit) is not None and environment.get("GITHUB_SHA") == commit
            and CORRELATION.fullmatch(environment.get("INPUT_CORRELATION", "")) is not None
            and environment.get("GITHUB_WORKFLOW_REF") == f"{REPOSITORY}/.github/workflows/{workflow}@{ref}"
            and ref in {line["sourceRef"] for line in active_lines(policy)},
            "maintenance-intent", "Maintenance source, workflow or active-line identity differs from dispatch.")

    return True


def source_file(commands, path, commit):
    """Read a small exact-commit workflow and verify its authenticated Git blob."""
    value = publication.api(commands, f"contents/{path}?ref={commit}")
    require(value.get("type") == "file" and value.get("path") == path and value.get("encoding") == "base64"
            and isinstance(value.get("content"), str) and len(value["content"]) <= 2 * 1024 * 1024,
            "maintenance-source", "Branch-native workflow is missing or malformed.")
    try:
        raw = base64.b64decode("".join(value["content"].split()), validate=True)
        text = raw.decode("ascii")
    except (ValueError, UnicodeError) as error:
        raise ReleaseError("maintenance-source", "Branch-native workflow encoding is invalid.") from error

    digest = hashlib.sha1(b"blob " + str(len(raw)).encode("ascii") + b"\0" + raw).hexdigest()
    require(value.get("sha") == digest, "maintenance-source", "Workflow differs from its exact Git blob.")

    return text


def literal(value):
    """Accept the deliberately simple scalars used by the repository's own CI matrix."""
    value = value.strip()
    if len(value) >= 2 and value[0] == value[-1] and value[0] in "\"'":
        value = value[1:-1]

    require(bool(value) and not any(token in value for token in ("${{", "\n", "&", "*", "{", "}")),
            "maintenance-matrix", "Unsupported CI matrix scalar.")

    return value


def qualification_producers(source):
    """Expand only NestedSet's literal CI shapes; unsupported matrix forms fail closed."""
    require(source.count("\njobs:\n") == 1, "maintenance-matrix", "Expected one CI jobs block.")
    jobs = source.split("\njobs:\n", 1)[1]
    starts = list(re.finditer(r"^  ([a-z][a-z0-9-]+):\s*$", jobs, re.MULTILINE))
    require(bool(starts) and len({item[1] for item in starts}) == len(starts),
            "maintenance-matrix", "Missing or repeated CI jobs.")
    names = []
    for index, match in enumerate(starts):
        body = jobs[match.end():starts[index + 1].start() if index + 1 < len(starts) else len(jobs)]
        display = re.findall(r"^    name: (.+)$", body, re.MULTILINE)
        require(len(display) <= 1, "maintenance-matrix", "Repeated CI display name.")
        name = display[0] if display else match[1]
        matrices = re.findall(r"^      matrix:\s*$", body, re.MULTILINE)
        if not matrices:
            names.append(literal(name))
            continue

        require(len(matrices) == 1 and match[1] in ("tests", "samples"),
                "maintenance-matrix", "Unexpected CI matrix producer.")
        if match[1] == "tests":
            require(name == "${{ matrix.name }}", "maintenance-matrix", "Test matrix display shape changed.")
            matrix = body.split("      matrix:\n", 1)[1].split("\n    steps:", 1)[0]
            require(matrix.startswith("        include:\n"), "maintenance-matrix", "Expected literal include rows.")
            rows = list(re.finditer(r"^          - ([a-z-]+): (.+)$", matrix, re.MULTILINE))
            require(bool(rows) and matrix.count("          - ") == len(rows),
                    "maintenance-matrix", "Unrecognized test matrix row.")
            require(not matrix[:rows[0].start()].strip().removeprefix("include:").strip(),
                    "maintenance-matrix", "Unconsumed test matrix fields.")
            ids, projects = set(), set()
            for row_index, row in enumerate(rows):
                end = rows[row_index + 1].start() if row_index + 1 < len(rows) else len(matrix)
                remainder = matrix[row.end():end]
                fields = [(row[1], row[2]), *re.findall(r"^            ([a-z-]+): (.+)$", remainder, re.MULTILINE)]
                remainder = re.sub(r"^            [a-z-]+: .+$", "", remainder, flags=re.MULTILINE)
                require(not remainder.strip() and len(fields) == 4
                        and set(dict(fields)) == {"id", "name", "project", "docker"},
                        "maintenance-matrix", "Incomplete or unsupported test matrix fields.")
                values = {key: literal(value) for key, value in fields}
                identity = values["id"]
                require(identity not in ids and values["project"] not in projects
                        and re.fullmatch(r"tests/[A-Za-z0-9.]+/[A-Za-z0-9.]+\.csproj", values["project"])
                        and values["docker"] in ("true", "false"),
                        "maintenance-matrix", "Repeated or malformed test producer.")
                ids.add(identity)
                projects.add(values["project"])
                names.append(values["name"])
        else:
            require(name == "SQLite sample / ${{ matrix.sample }}",
                    "maintenance-matrix", "Sample matrix display shape changed.")
            matrix = body.split("      matrix:\n", 1)[1].split("\n    steps:", 1)[0].strip()
            values = re.fullmatch(r"sample: \[([A-Za-z0-9_, ]+)\]", matrix)
            require(values is not None, "maintenance-matrix", "Unsupported sample matrix.")
            samples = [literal(value) for value in values[1].split(",")]
            require(len(samples) == len(set(samples)), "maintenance-matrix", "Duplicate sample producer.")
            names.extend("SQLite sample / " + sample for sample in samples)

    require({item[1] for item in starts} >= {"schedule-identity", "tests", "samples", "source-quality",
            "engineering-tests", "build", "coverage", "repository-qualification"}
            and len(names) == len(set(names)), "maintenance-matrix", "Missing or duplicate qualification producer.")

    return names


def producer_names(commands, workflow, commit):
    """Derive actual CI producers or the two fixed audit producers from exact source."""
    source = source_file(commands, f".github/workflows/{workflow}", commit)
    if workflow == "ci.yml":
        return qualification_producers(source)

    require(workflow == WORKFLOW and re.search(r"^  schedule-identity:", source, re.MULTILINE)
            and re.search(r"^  dependency-vulnerability-audit:", source, re.MULTILINE),
            "maintenance-producers", "Native audit producers are missing.")

    return ["schedule-identity", "dependency-vulnerability-audit"]


def verify_jobs(commands, run_id, attempt, expected):
    """Check every actual producer and the complete paginated job inventory for that attempt."""
    jobs, seen, total = [], set(), None
    for page in range(1, 102):
        value = publication.api(commands, f"actions/runs/{run_id}/attempts/{attempt}/jobs?per_page=100&page={page}")
        entries, count = value.get("jobs"), value.get("total_count")
        require(type(count) is int and 0 < count <= 10000 and isinstance(entries, list) and len(entries) <= 100
                and (total is None or total == count), "maintenance-jobs", "Incomplete or changed job inventory.")
        total = count
        for item in entries:
            require(isinstance(item, dict), "maintenance-jobs", "Malformed job entry.")
            identity = positive_number(item.get("id"))
            require(identity not in seen, "maintenance-jobs", "Duplicate job identity.")
            seen.add(identity)
            jobs.append(item)

        require(len(jobs) <= total, "maintenance-jobs", "Job pagination exceeds its declared total.")
        if len(entries) < 100:
            require(len(jobs) == total, "maintenance-jobs", "Missing job pages.")
            break
    else:
        raise ReleaseError("maintenance-jobs", "Job pagination exceeds its safety bound.")

    for name in expected:
        matches = [job for job in jobs if job.get("name") == name]
        require(len(matches) == 1 and matches[0].get("status") == "completed"
                and matches[0].get("conclusion") == "success", "maintenance-producer",
                f"Required producer '{name}' is missing, duplicated, skipped or unsuccessful.")


def wait_for_child(commands, run_id, workflow, ref, commit, correlation, expected, *,
                   timeout=14400, sleep=time.sleep, clock=time.monotonic, record=lambda update: None):
    """Keep queue and execution bounded while validating each actual child observation."""
    deadline = clock() + timeout
    while True:
        run = publication.api(commands, f"actions/runs/{run_id}")
        require(run.get("id") == run_id and run.get("event") == "workflow_dispatch"
                and run.get("head_sha") == commit and run.get("head_branch") == ref.removeprefix("refs/heads/")
                and run.get("path", "").split("@", 1)[0] == f".github/workflows/{workflow}"
                and run.get("repository", {}).get("full_name") == REPOSITORY
                and run.get("display_title") == f"maintenance {correlation}",
                "maintenance-child", "Child does not match the exact dispatched workflow/source/correlation.")
        record({"status": run.get("status"), "conclusion": run.get("conclusion")})
        if run.get("status") == "completed":
            require(run.get("conclusion") == "success", "maintenance-child", "Scheduled child did not succeed.")
            verify_jobs(commands, run_id, positive_number(run.get("run_attempt")), expected)

            return {"sourceRef": ref, "sourceCommit": commit, "runId": run_id,
                    "workflow": workflow, "verificationStatus": "success"}

        require(run.get("status") in ("queued", "in_progress", "waiting", "pending", "requested"),
                "maintenance-child", "Unrecognized child execution state.")
        require(clock() < deadline, "maintenance-timeout", "Scheduled child exceeded its queue/execution budget.")
        sleep(min(15, max(0, deadline - clock())))


def dispatch_line(commands, workflow, ref, correlation, *, record=lambda update: None):
    """Dispatch the protected exact head and use the returned run ID without a racy search."""
    branch = ref.removeprefix("refs/heads/")
    observed = publication.api(commands, f"branches/{quote(branch, safe='')}")
    commit = observed.get("commit", {}).get("sha", "")
    require(observed.get("name") == branch and observed.get("protected") is True and SHA.fullmatch(commit),
            "maintenance-branch", "Active maintenance source is missing, unprotected or malformed.")
    record({"sourceCommit": commit, "verificationStatus": "resolved"})
    expected = producer_names(commands, workflow, commit)
    response = publication.api_request(commands, f"actions/workflows/{workflow}/dispatches", "POST", {
        "ref": branch, "inputs": {"maintenance": True, "schedule-correlation": correlation,
                                    "schedule-source-sha": commit}})
    # WHY: An empty legacy dispatch response cannot bind an exact child. Never search overlapping runs by time.
    run_id = positive_number(response.get("workflow_run_id"))
    record({"runId": run_id, "verificationStatus": "dispatched"})

    return wait_for_child(commands, run_id, workflow, ref, commit, correlation, expected, record=record)


def coordinate_lines(commands, authority, workload, environment, output):
    """Dispatch active source lines once and retain independent failures without suppressing siblings."""
    policy = authority["policy"]
    require(workload in ("audit", "qualification")
            and environment.get("GITHUB_EVENT_NAME") in ("schedule", "workflow_dispatch")
            and environment.get("GITHUB_REF") == policy["authorityRef"]
            and environment.get("GITHUB_SHA") == authority["commit"]
            and environment.get("GITHUB_REPOSITORY") == REPOSITORY
            and environment.get("GITHUB_WORKFLOW_REF") == f"{REPOSITORY}/.github/workflows/{WORKFLOW}@{policy['authorityRef']}",
            "maintenance-coordinator", "Only current protected-main maintenance can coordinate source lines.")
    run, attempt = environment.get("GITHUB_RUN_ID", ""), environment.get("GITHUB_RUN_ATTEMPT", "")
    correlation = f"schedule-{run}-{attempt}"
    require(CORRELATION.fullmatch(correlation), "maintenance-identity", "Invalid coordinator run identity.")
    targets = active_lines(policy)
    workflow = "ci.yml" if workload == "qualification" else WORKFLOW
    report = {"schemaVersion": 1, "authorityCommit": authority["commit"], "workload": workload,
              "correlation": correlation, "conclusion": "in_progress",
              "children": [{"sourceRef": line["sourceRef"], "workflow": workflow,
                            "verificationStatus": "pending"} for line in targets]}
    lock = threading.Lock()

    def record(index, update):
        """Serialize only diagnostic writes while each branch owns its actual workload."""
        with lock:
            report["children"][index].update(update)
            write_json(output, report)

    write_json(output, report)
    if targets:
        with concurrent.futures.ThreadPoolExecutor(max_workers=len(targets)) as executor:
            futures = [(index, executor.submit(dispatch_line, commands, workflow, line["sourceRef"], correlation,
                       record=lambda update, owner=index: record(owner, update))) for index, line in enumerate(targets)]
            for index, future in futures:
                try:
                    record(index, future.result())
                except (ReleaseError, OSError, subprocess.SubprocessError) as error:
                    record(index, {"verificationStatus": "failed", "reason": str(error)})

    report["conclusion"] = "success" if all(child["verificationStatus"] == "success"
                                          for child in report["children"]) else "failure"
    write_json(output, report)

    return report


def validate_audit(report, expected_projects):
    """Require a complete clean direct/transitive audit, not only an exit-zero command."""
    require(isinstance(report, dict) and type(report.get("version")) is int and report["version"] == 1,
            "maintenance-audit", "Unknown audit schema.")
    parameters = report.get("parameters", "")
    require(isinstance(parameters, str)
            and all(re.search(r"(?:^|\s)" + flag + r"(?:\s|$)", parameters)
                    for flag in ("--vulnerable", "--include-transitive"))
            and report.get("sources") == ["https://api.nuget.org/v3/index.json"],
            "maintenance-audit", "Incomplete audit scope or advisory source.")
    projects = report.get("projects")
    require(isinstance(projects, list) and projects, "maintenance-audit", "No audited project inventory.")
    observed = []
    for value in [report, *projects]:
        require(isinstance(value, dict) and all(value.get(key, []) == [] for key in ("problems", "warnings", "errors")),
                "maintenance-audit", "Advisory collection reported incomplete evidence.")

    for project in projects:
        require(isinstance(project.get("path"), str), "maintenance-audit", "Missing project identity.")
        observed.append(str(Path(project["path"]).resolve()))
        frameworks = project.get("frameworks", [])
        # WHY: Genuine clean vulnerability reports may omit finding-only framework collections.
        require(isinstance(frameworks, list), "maintenance-audit", "Malformed framework inventory.")
        seen_frameworks = set()
        for framework in frameworks:
            require(isinstance(framework, dict) and isinstance(framework.get("framework"), str)
                    and framework["framework"] and framework["framework"] not in seen_frameworks
                    and all(framework.get(key, []) == [] for key in ("problems", "warnings", "errors")),
                    "maintenance-audit", "Missing, duplicate or failed framework evidence.")
            seen_frameworks.add(framework["framework"])
            for kind in ("topLevelPackages", "transitivePackages"):
                packages = framework.get(kind, [])
                require(isinstance(packages, list), "maintenance-audit", "Malformed package inventory.")
                seen = set()
                for package in packages:
                    require(isinstance(package, dict) and isinstance(package.get("id"), str)
                            and package["id"] and package["id"] not in seen
                            and isinstance(package.get("resolvedVersion"), str) and package["resolvedVersion"]
                            and package.get("vulnerabilities") == [],
                            "maintenance-audit", "Incomplete package evidence or known vulnerability.")
                    seen.add(package["id"])

    require(len(observed) == len(set(observed)) and len(expected_projects) == len(set(expected_projects))
            and set(observed) == {str(Path(path).resolve()) for path in expected_projects},
            "maintenance-audit", "Audit does not cover the exact solution project graph.")

    return {"schemaVersion": 1, "conclusion": "success", "projectCount": len(observed)}


def run_audit(root, output):
    """Refresh the branch-owned graph and retain raw restore/advisory evidence in a fresh directory."""
    expected = solution_projects(root)
    require(not output.exists() or not any(output.iterdir()), "maintenance-audit", "Audit output must be fresh.")
    output.mkdir(parents=True, exist_ok=True)
    write_json(output / "summary.json", {"schemaVersion": 1, "conclusion": "in_progress"})
    environment = {**os.environ, "NUGET_HTTP_CACHE_PATH": str((output / "http-cache").resolve())}
    restore = ["dotnet", "restore", str(root / SOLUTION), "--locked-mode", "--force", "--no-http-cache",
               "--disable-build-servers", "-p:NuGetAudit=true", "-p:NuGetAuditMode=all", "-p:NuGetAuditLevel=low",
               "-p:TreatWarningsAsErrors=true", "-p:NoWarn=", "-p:WarningsNotAsErrors=",
               "--source", "https://api.nuget.org/v3/index.json"]
    stage = "restore"
    try:
        with (output / "restore.log").open("w", encoding="utf-8") as log:
            result = subprocess.run(restore, cwd=root, env=environment, stdout=log, stderr=subprocess.STDOUT,
                                    check=False, timeout=1200)

        require(result.returncode == 0, "maintenance-audit", "Fresh audited restore failed; retain its log.")
        stage = "advisories"
        command = ["dotnet", "package", "list", "--project", str(root / SOLUTION), "--no-restore", "--vulnerable",
                   "--include-transitive", "--format", "json", "--output-version", "1",
                   "--source", "https://api.nuget.org/v3/index.json"]
        with (output / "audit.json").open("w", encoding="utf-8") as report, \
                (output / "audit.log").open("w", encoding="utf-8") as errors:
            result = subprocess.run(command, cwd=root, env=environment, stdout=report, stderr=errors,
                                    check=False, timeout=1200)

        require(result.returncode == 0 and not (output / "audit.log").read_text().strip(),
                "maintenance-audit", "Vulnerability command failed or emitted warnings; retain its evidence.")
        stage = "validation"
        summary = validate_audit(read_json(output / "audit.json"), expected)
        write_json(output / "summary.json", summary)
    except (ReleaseError, OSError, subprocess.SubprocessError) as failure:
        # WHY: A completed failed command must not leave an ongoing/successful audit claim. Retain raw
        # diagnostics separately; only the fixed operation stage and named error belong in this summary.
        write_json(output / "summary.json", {"schemaVersion": 1, "conclusion": "failure", "stage": stage,
                                             "errorCode": failure.code if isinstance(failure, ReleaseError)
                                             else "command-failed"})
        raise

    return summary


def dependency_entries(source):
    """Read only the retained version-2 update-list shape, without another YAML dependency."""
    meaningful = "\n".join(line for line in source.splitlines() if line.strip() and not line.lstrip().startswith("#"))
    if meaningful == "version: 2\nupdates: []":
        return []

    require(source.startswith("version: 2\nupdates:\n"), "dependency-template", "Expected version-2 update entries.")
    starts = list(re.finditer(r"^  - package-ecosystem: ([a-z-]+)\s*$", source, re.MULTILINE))
    require(starts and len(starts) == len(re.findall(r"package-ecosystem\s*:", source)),
            "dependency-template", "Unsupported update entry shape.")
    prefix = "\n".join(line for line in source[:starts[0].start()].splitlines()
                       if line.strip() and not line.lstrip().startswith("#"))
    require(prefix == "version: 2\nupdates:", "dependency-template", "Unconsumed update-list fields.")
    result = []
    for index, match in enumerate(starts):
        body = source[match.start():starts[index + 1].start() if index + 1 < len(starts) else len(source)]
        target = re.findall(r"^    target-branch: ([^\n]+)$", body, re.MULTILINE)
        require(len(target) <= 1, "dependency-template", "Repeated update target.")
        branch = literal(target[0]) if target else "main"
        require(re.fullmatch(r"main|maintenance/[1-9][0-9]*\.x|preview/[1-9][0-9]*\.x", branch),
                "dependency-template", "Update target must be an exact canonical branch.")
        result.append((match[1], branch, body))

    return result


def render_dependencies(policy, template):
    """Render active targets from lifecycle-independent grouping and Docker LTS settings."""
    entries = dependency_entries(template)
    require(len(entries) == len(ECOSYSTEMS) and {kind for kind, _, _ in entries} == set(ECOSYSTEMS)
            and all(branch == "main" and "target-branch:" not in body for _, branch, body in entries),
            "dependency-template", "Template must retain each of the four default ecosystems once.")
    targets = active_lines(policy)
    if not targets:
        return "version: 2\nupdates: []\n"

    result = "version: 2\nupdates:\n"
    for line in targets:
        branch = line["sourceRef"].removeprefix("refs/heads/")
        for ecosystem, _, body in entries:
            header, rest = body.split("\n", 1)
            block = f"{header}\n    target-branch: {branch}\n{rest.rstrip()}\n"
            if ecosystem in ("dotnet-sdk", "nuget"):
                require(not re.search(r"^    ignore:", body, re.MULTILINE),
                        "dependency-template", "Framework ignore settings belong to the reviewed line generator.")
                block += "    ignore:\n"
                if ecosystem == "dotnet-sdk":
                    block += ('      - dependency-name: "*"\n        update-types:\n'
                              '          - version-update:semver-major\n')
                else:
                    for package in FRAMEWORK_PACKAGES:
                        # WHY: Dependabot accepts leading-hyphen NuGet identifiers that sort before numeric -0.
                        block += (f'      - dependency-name: "{package}"\n'
                                  f'        versions: [">={line["packageMajor"] + 1}.0.0-0", '
                                  f'">={line["packageMajor"] + 1}.0.0--"]\n')

            result += block

    return result


def check_dependencies(policy, source, template):
    """Detect missing targets and changes to grouping, major ceilings or qualified database lines."""
    def meaningful(text):
        return "\n".join(line for line in text.splitlines() if line.strip() and not line.lstrip().startswith("#"))

    require(meaningful(source) == meaningful(render_dependencies(policy, template)),
            "dependency-targets", "Dependabot differs from active release lines or its reviewed template.",
            "Review the template/policy, then render-dependencies and commit the generated configuration through a PR.")


def check_pr(commands, event):
    """Permit registered preparation bases, with a main-only authenticated first-install bridge."""
    pull = event.get("pull_request", {})
    base, head = pull.get("base", {}), pull.get("head", {})
    require(base.get("repo", {}).get("full_name") == REPOSITORY and isinstance(base.get("ref"), str)
            and SHA.fullmatch(base.get("sha", "")) and SHA.fullmatch(head.get("sha", "")),
            "release-pr", "Dependency review requires exact repository base/head identities.")
    authority = publication.observe_authority(commands, allow_missing=True)
    if authority is None:
        require(base["ref"] == "main", "release-pr", "Only main can bootstrap an authenticated missing line policy.")
    else:
        from eng.release.lines import select_line

        select_line(authority["policy"], "refs/heads/" + base["ref"], purpose="pr")

    return {"baseRef": base["sha"], "headRef": head["sha"]}


def main():
    """Expose only actual workflow and reviewed configuration maintenance operations."""
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    coordinate = commands.add_parser("coordinate")
    coordinate.add_argument("--workload", choices=("audit", "qualification"), required=True)
    coordinate.add_argument("--output", type=Path, required=True)
    guard = commands.add_parser("guard")
    guard.add_argument("--workflow", choices=("ci.yml", WORKFLOW), required=True)
    commands.add_parser("check-pr")
    audit = commands.add_parser("audit")
    audit.add_argument("--output", type=Path, required=True)
    render = commands.add_parser("render-dependencies")
    render.add_argument("--output", type=Path, required=True)
    commands.add_parser("check-dependencies")
    args = parser.parse_args()
    executor = publication.Commands()
    if args.command in ("render-dependencies", "check-dependencies"):
        from eng.release.lines import load_policy

        policy = load_policy()
        template = (ROOT / ".github/dependabot-template.yml").read_text(encoding="ascii")
        if args.command == "render-dependencies":
            args.output.write_text(render_dependencies(policy, template), encoding="ascii")
        else:
            check_dependencies(policy, (ROOT / ".github/dependabot.yml").read_text(encoding="ascii"), template)
    elif args.command == "guard":
        if os.environ.get("INPUT_MAINTENANCE", "false") == "false":
            return 0

        authority = publication.observe_authority(executor)
        validate_child(authority["policy"], args.workflow, os.environ)
    elif args.command == "check-pr":
        check_pr(executor, read_json(os.environ["GITHUB_EVENT_PATH"]))
    elif args.command == "audit":
        run_audit(ROOT, args.output)
    else:
        report = coordinate_lines(executor, publication.observe_authority(executor),
                                  args.workload, os.environ, args.output)
        require(report["conclusion"] == "success", "maintenance-failed", "One or more native source lines failed.")

    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (ReleaseError, OSError, subprocess.SubprocessError) as failure:
        print(failure, file=sys.stderr)
        sys.exit(1)

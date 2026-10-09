# Release process

This document defines the package and evidence contract. The operator sequence
is in [Release publication](operations/release-publication.md), and decision
authority is in [Release governance](release-governance.md).

Version `10.0.0` is the first stable contract. Each version, including the
earlier `10.0.0-rc.1`, retains its own exact packages and qualification evidence.
RC archives are never renamed into stable packages.

The operator entry point is `./eng/pre-tag-check.sh`, as in Doka and
SafeMigrations. It checks readiness from the current `main` checkout before the
untagged hosted candidate starts. The runbook uses `release_commit`,
`release_version`, and `release_tag` throughout checkout, tag creation, and
public release inspection.

## Release identity

One version publishes two package IDs:

- `Doka.NestedSet`;
- `Doka.EntityFrameworkCore.NestedSet`.

The workflow accepts a canonical `X.Y.Z-rc.N` or `X.Y.Z` version without a
leading `v`. It must match the reviewed source version and one dated changelog
section. Both NuGet identities must be unused, the release tag must not exist,
and dispatch must target exact current protected `main`.

## Qualification stages

Pull-request CI checks the offline engineering regressions, shipping package locks,
C# style, Release build, all test projects, coverage reports, runnable samples,
pack, and primary/symbol archive inspection. The PR-only lockfile diagnostic
tests run there. The release candidate independently repeats the solution and
release-tooling checks from its selected `main` commit and additionally verifies
isolated package consumers and SBOMs. It checks source, dependency locks, and
compiled binaries throughout qualification and seals the candidate only after
every check passes. It consumes no prior CI artifacts.

The local `eng/release-candidate.sh` entry point runs all checks serially by
default. A failed local run requires a fresh output directory and a complete
rerun. Hosted qualification uses the same checks through fixed `--job` selectors
and this GitHub job graph:

| Job | Direct prerequisites | Work |
| --- | --- | --- |
| `preflight` | Dispatch | Verify exact source and unused version; establish one shared 7,200-second UTC deadline |
| `source-quality` | `preflight` | Locked restore, C# style, and unused imports |
| `engineering-tests` | `preflight` | Offline RC-tooling regressions and shell syntax |
| `build` | `preflight` | Canonical Release solution build; pack each shipping package once; inspect primary and symbol archives |
| `tests` | `build` | Nine executable test projects on separate runners, using compiled DLLs and coverage |
| `samples` | `build` | Three compiled SQLite samples: FileSystem, Kpis, and UserGroups |
| `consumer`, `sbom` | `build` | Independently verify exact package consumers and both SBOMs |
| `qualify` | All jobs above | Validate complete evidence and assemble the unchanged candidate |

The build TAR preserves complete execution files, hidden mapping files, the
isolated coverage collector, and executable modes. Consumers and SBOMs download
a separate small package artifact. Build and package downloads use the exact
producer's nonempty artifact ID. Test and sample matrices use `fail-fast: false` without a
repository-imposed parallelism cap; their Docker and result directories are
isolated. They do not rebuild or repack the candidate. The shared specification
library is not an executable test project.

Only source quality and the canonical build use setup-dotnet's default NuGet
cache, because they restore into that cache. Tests and samples execute compiled
files; consumers and SBOMs restore into isolated caches. These four jobs set up
the pinned SDK without restoring or saving an unused default package cache.

`qualify` checks GitHub's `needs` results with `jq` and rejects any prerequisite
result other than `success`. Four scalar artifact IDs from the source-quality,
engineering, consumer, and SBOM job outputs select those downloads directly.
Test and sample outputs use only the current run-and-attempt name prefix and
merge into the workspace. All evidence uploads include hidden files, and the
pinned download action verifies artifact digests. There is no separate job
receipt schema, artifact-index API, or duplicate role/file inventory.

The existing source, version, workflow run, producer attempt, dependency-lock,
runtime, and package guards still apply. Final qualification requires all nine
test-project results, coverage for every project and executed lines in both
shipping modules across all reports, every query plan, and all three successful
sample results. Consumer jobs verify the exact package hashes; final qualification
requires their successful version-matching core-only and EF results and compares
the recorded primary-package hashes with the inspected packages being sealed. The successful
source/tooling jobs retain command logs, and qualification rechecks SBOM evidence
with the existing offline verifier. The final job does
not restore dependencies or set up .NET.
Only then does it seal the candidate and pass its artifact ID unchanged to
`attest` and `publish`. Every job derives its remaining budget from the same UTC
deadline; the two-hour allowance does not restart per job.

Only the two shipping projects under `src/` commit dependency locks. RC
qualification copies those locks into its owned workspace and verifies that
restore preserves them. Tests, samples, benchmarks, and tools restore their
own graphs without additional committed lockfiles.

| Stage | Required evidence |
| --- | --- |
| `quality` | Offline release-tooling tests, shell syntax, reviewed shipping package locks, Roslyn style/import checks, warning-free Release build |
| `tests` | Docker readiness, every test project, all provider cells, ordinary and optional migrations, coverage artifacts, query plans, and runnable samples |
| `packages` | One pack per shipping package from the canonical build; exact primary/symbol metadata, assembly, PDB, XML docs, source, dependencies, and archive inventory |
| `consumer` | Independent core-only and EF/SQLite applications restore and execute the exact archives |
| `sbom` | Standalone Microsoft SBOM binary generates and validates one SPDX 2.2 dependency closure per primary package; negative verifier cases run in offline tests |

These stage names group completed evidence; they do not require serial hosted
execution. Packages produced before the tests finish are qualification inputs
until every required check passes and `qualify` seals them.

CPU time, wall-clock benchmark results, working set, and process-wide
allocation are not release gates. Deterministic command, row, plan, memory-
ownership, and correctness regressions belong in the test suites.
Decision-record validation is a separate maintainer check, not a CI or RC qualification gate.

The final candidate contains:

- two `.nupkg` files;
- two `.snupkg` files;
- two package-specific SPDX JSON documents;
- `package-manifest.json`;
- `qualification-evidence.zip`;
- `release-notes.md`; and
- `candidate.json`, which inventories every other file by size and SHA-256.

A workspace qualification can verify uncommitted changes but sets
`publishable: false`. Only the exact hosted release workflow on current `main`
can produce a publishable candidate.

## Attestation

After qualification, GitHub's attestation action signs:

- `candidate.json`, both primary packages, and both symbol packages as SLSA v1
  build-provenance subjects; and
- each primary package with its exact SPDX 2.2 predicate.

The workflow retains portable bundles and verifies their signatures, subject
names and digests, repository, workflow path, workflow commit, `main` source
ref/commit, run attempt, and GitHub-hosted runner boundary before publication.

## Publication boundary

The `publish` job is the only job with `contents: write`, NuGet OIDC, and the
protected `nuget` environment. Before requesting credentials it verifies:

- unchanged candidate and provenance inventories;
- exact hosted repository, workflow, run, attempt, source, and current `main`;
- a reviewed signed annotated tag that peels to the candidate commit;
- repository and branch protection visibility;
- unused or same-candidate public package state; and
- a complete matching draft GitHub release with every candidate and provenance
  asset.

The pinned official `NuGet/login` action obtains a short-lived credential for
the configured trusted publisher. The workflow pushes both primary packages in
dependency order (Core, then EF), followed by both symbol packages in the same
order. A symbol upload failure therefore cannot prevent the dependent EF
primary package from being published. Public readback then verifies both
packages and their Portable PDBs.

Public package archives contain NuGet.org's repository signature and therefore
do not have the same raw ZIP hash as the unsigned candidate. Acceptance
requires a valid repository signature plus canonical archive-content equality
after excluding only `.signature.p7s`.

The public package and symbol readback has a 120-minute shared deadline within
the 180-minute publish job. NuGet's temporary publishing credential is requested
immediately before the uploads; readback does not use it. A deadline failure
leaves the release incomplete and requires the same-run publish-job recovery.
GitHub release and asset attestation retries share one bounded budget rather
than restarting that budget for each asset.

The GitHub release stays a draft until NuGet readback succeeds. It is then
published and verified in immutable state, including GitHub's release/asset
attestation behavior where available.

## Recovery

Before sealing, repeat qualification with **Re-run all jobs** or a new dispatch.
This operator recommendation refreshes every check and the preflight deadline.
The code enforces a narrower boundary: downloaded build identity must match the
current source, version, run, attempt, and deadline; test/sample downloads select
the current run and attempt. Consumer and SBOM evidence must match the package
bytes being sealed.

Successful `source-quality` and `engineering-tests` jobs can retain logs from an
earlier attempt of the same run and commit, selected by their scalar artifact
IDs. For example, if only `build` fails, **Re-run failed jobs** reruns the build
and its dependent jobs while retaining those independent successful checks.
Their logs are evidence about the source and tooling, not inputs to the compiled
runtime or package archives. Qualification does not require those logs to come
from the current attempt. This case follows from the job graph and GitHub's
rerun behavior; it has not been exercised in a hosted run.

A partial rerun that retains `preflight` also retains its original deadline; it
does not receive another two-hour budget. Reusing a previous attempt's build for
new test/sample jobs fails the build-identity check, so partial reruns are not a
general recovery path. Source changes require a new reviewed candidate. Once
sealed, the existing attestation/publication recovery continues to use the
original candidate artifact IDs and exact bytes.

NuGet cannot atomically publish two package IDs. If a failure occurs after one
upload, rerun only the failed `publish` job in the original workflow run. The
same candidate and provenance artifact IDs are downloaded again. Matching
public packages can be skipped or pushed with duplicate tolerance; conflicting
content fails permanently.

A new dispatch is rejected once a package version or tag exists. Never rebuild
missing files, replace a release asset, move a tag, delete a package version,
or reuse a version for different bytes.

If GitHub release finalization or platform attestation times out after NuGet
success, retry the same failed job. The workflow revalidates every external
state before continuing.

## Evidence limits

Local qualification proves local execution only. Hosted qualification proves
the exact hosted source and candidate only. A completed release additionally
requires tag verification, protected approval, short-lived credential exchange,
NuGet signing/readback, and immutable GitHub release readback.

None of these prove an application's authorization, schema customizations,
backup restore, production workload capacity, or direct SQL discipline.

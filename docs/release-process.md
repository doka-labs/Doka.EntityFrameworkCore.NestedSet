# Release process

This document defines the package and evidence contract. The operator sequence
is in [Release publication](operations/release-publication.md), and decision
authority is in [Release governance](release-governance.md).
The [release-line contract](operations/release-lines.md) governs exact branch
eligibility, framework ownership, preparation and ongoing servicing.

Version `10.1.0` is the current stable six-package contract; `10.0.0` is the
first stable contract. Each version, including the
earlier `10.0.0-rc.1`, retains its own exact packages and qualification evidence.
RC archives are never renamed into stable packages.

The operator entry point is `./eng/pre-tag-check.sh`, as in Doka and
SafeMigrations. It checks readiness from the current authorized branch before the
untagged hosted candidate starts. The runbook uses `release_commit`,
`release_version`, and `release_tag` throughout checkout, tag creation, and
public release inspection.

## Release identity

One version publishes six package IDs in dependency order:

- `Doka.NestedSet`;
- `Doka.EntityFrameworkCore.NestedSet`;
- `Doka.EntityFrameworkCore.NestedSet.MySql`;
- `Doka.EntityFrameworkCore.NestedSet.Postgres`;
- `Doka.EntityFrameworkCore.NestedSet.Sqlite`; and
- `Doka.EntityFrameworkCore.NestedSet.SqlServer`.

The workflow accepts a canonical `X.Y.Z-rc.N` or `X.Y.Z` version without a
leading `v`. It must match the reviewed source version and one dated changelog
section. All six NuGet identities must be unused, the release tag must not exist,
and dispatch must target that line's exact current protected head. The version
major must match the line's approved framework/toolchain and package major.
Current source is .NET 10 with `10.1.0`; additional lines remain inactive.

## Qualification stages

Pull-request CI and the release candidate call the same
[quality workflow](../.github/workflows/quality-gates.yml). Both run the
offline engineering regressions and shell syntax, shipping package locks, C#
style, the Release build with pack and primary/symbol archive inspection,
isolated package consumers, SBOMs, all test projects, results, query plans and
coverage, and runnable samples. The Dependabot lockfile diagnostic runs only
for pull requests. The release candidate starts from its selected
branch/commit, consumes no prior CI artifacts, and seals the candidate only
after every shared job passes. Complete qualification runs only on hosted
runners; there is no local complete qualification.

The release candidate's GitHub job graph:

| Job | Direct prerequisites | Work |
| --- | --- | --- |
| `preflight` | Dispatch | Verify current line authorization, exact protected source, compatible toolchain, unused version and dated release notes; freeze line identity |
| `Qualification / C# style and unused imports` | `preflight` | Locked restore, C# style, and unused imports |
| `Qualification / Engineering regression tests` | `preflight` | Offline release-tooling and Dependabot-diagnostic regressions, shell syntax, active-line dependency targets |
| `Qualification / Release build and NuGet packages` | `preflight` | Release solution build with the exact version and owned locks; pack each shipping package once; inspect primary and symbol archives |
| `Qualification / Verify exact package consumers`, `Qualification / Generate and verify package SBOMs` | build | Independently verify six isolated package roots, the FileSystem consumer, and all six SBOMs from the build's package artifact |
| `Qualification / <test cell>` | `preflight` | Thirteen cells for nine executable test projects; each restores, builds, and tests its project with coverage |
| `Qualification / SQLite sample / <sample>` | `preflight` | FileSystem, SignalLab, and UserGroups |
| `Qualification / Test evidence and coverage` | test cells | Passing results for every project without repeated cases, every provider plan family, and coverage of all six shipping modules |
| `qualify` | `preflight`, the shared call | Validate complete evidence and assemble the unchanged candidate |

Pull-request CI runs the same `Qualification / ...` jobs after
`schedule-identity`; its `Repository qualification` gate requires them to
succeed. Packages downloaded by consumers, SBOMs, and the seal use the build's
exact nonempty artifact ID. Test and sample matrices use `fail-fast: false`
without a repository-imposed parallelism cap; their Docker and result
directories are isolated. The shared MySQL/MariaDB project runs as five
filtered cells whose VSTest filters partition it, so each test runs exactly
once. The shared specification library is not an executable test project.

Source quality, the build, test cells, and samples restore into setup-dotnet's
default NuGet cache. Consumers and SBOMs restore into isolated caches and set up
the pinned SDK without restoring or saving an unused default package cache.

`qualify` checks GitHub's `needs` results with `jq` and rejects any prerequisite
result other than `success`. The package, consumer, and SBOM artifact IDs from
the shared call's outputs select those downloads directly. Test artifacts use
only the current run-and-attempt name prefix and keep one directory per cell.
Test, consumer, and SBOM evidence uploads include hidden files, and the pinned
download action verifies artifact digests. There is no separate job receipt
schema, artifact-index API, or duplicate role/file inventory.

The existing source, version, workflow run, producer attempt, dependency-lock,
and package guards still apply. Final qualification requires passing results
for all nine executable test projects, no case repeated across cells, executed
lines in all six shipping modules across all reports, and every query-plan
family. Consumer jobs verify the exact package hashes; final qualification
requires their successful version-matching `core`, `ef`, `mysql`, `postgres`,
`sqlite`, `sqlserver`, and `filesystem` results and compares the recorded
primary-package hashes with the inspected packages being sealed. Qualification
rechecks SBOM evidence with the existing offline verifier. The final job does
not restore dependencies or set up .NET. Only then does it seal the candidate
and pass its artifact ID unchanged to `attest` and `publish`. Job timeouts and a
bounded per-process budget limit each job; there is no shared deadline.

All six shipping projects under `src/` commit dependency locks. RC
qualification copies those locks into its owned workspace and verifies that
restore preserves them. Tests, samples, benchmarks, and tools restore their
own graphs without additional committed lockfiles.

| Stage | Required evidence |
| --- | --- |
| `quality` | Offline release-tooling tests, shell syntax, reviewed shipping package locks, Roslyn style/import checks, warning-free Release build |
| `tests` | Docker readiness, every test project, all provider cells, ordinary and optional migrations, coverage artifacts, query plans, and runnable samples |
| `packages` | One pack per shipping package from the release build; exact primary/symbol metadata, assembly, PDB, XML docs, source, dependencies, and archive inventory |
| `consumer` | Six isolated package roots plus the actual FileSystem sample restore the exact archives; bounds/SQLite/sample execution and real provider API/model/query translation are checked without external databases |
| `sbom` | Standalone Microsoft SBOM binary generates and validates one SPDX 2.2 dependency closure per primary package; negative verifier cases run in offline tests |

These stage names group completed evidence; they do not require serial hosted
execution. Test cells build their own projects from the same commit; the packed
assemblies are bound by the same source, locked graph, and archive inspection.
Packages produced before the tests finish are qualification inputs until every
required check passes and `qualify` seals them.

CPU time, wall-clock benchmark results, working set, and process-wide
allocation are not release gates. Deterministic command, row, plan, memory-
ownership, and correctness regressions belong in the test suites.
Decision-record validation is a separate maintainer check, not a CI or RC qualification gate.

The final candidate contains:

- six `.nupkg` files;
- six `.snupkg` files;
- six package-specific SPDX JSON documents;
- `package-manifest.json`;
- `qualification-evidence.zip`;
- `release-notes.md`; and
- `candidate.json`, which inventories every other file by size and SHA-256.

Pull-request CI builds the same packages without a release line and cannot
seal a candidate. Only the exact hosted release workflow on an authorized
active line can produce a publishable candidate. `qualification-only=true`
permits complete nonpublishing qualification of prepared or active lines and
skips the entire publisher environment. The frozen line travels with existing
identity/candidate evidence, not another per-job inventory or release registry.

## Attestation

After qualification, GitHub's attestation action signs:

- `candidate.json`, all six primary packages, and all six symbol packages as SLSA v1
  build-provenance subjects; and
- each primary package with its exact SPDX 2.2 predicate.

The workflow retains portable bundles and verifies their signatures, subject
names and digests, repository, workflow path, workflow commit, original source
ref/commit, run attempt, and GitHub-hosted runner boundary before publication.

## Publication boundary

The `publish` job is the only job with `contents: write`, NuGet OIDC, and the
protected `nuget` environment. Before requesting credentials it verifies:

- unchanged candidate and provenance inventories;
- exact hosted repository, workflow, run, attempt and qualified source line;
- current independent protected-main permission for that exact ref, major and
  channel, plus the selected line's protection and source history;
- a reviewed signed annotated tag that peels to the candidate commit;
- repository and branch protection visibility;
- unused or same-candidate public package state; and
- a complete matching draft GitHub release with every candidate and provenance
  asset.

The pinned official `NuGet/login` action obtains a short-lived credential for
the configured trusted publisher. The workflow pushes all six primary packages
in dependency order: pure core, common EF kernel, MySql, Postgres, Sqlite, and
SqlServer. Only then does it push all six symbol packages in the same order.
A symbol upload failure therefore cannot prevent any dependent primary package
from being uploaded. Public readback verifies every primary package and its
Portable PDB.

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

All publishers share one non-cancelling queue across versions and lines.
Before irreversible writes, they recheck current protected-main authority.
Stable GitHub Latest is promoted only when canonical version precedence
permits it; an older stable maintenance major or patch remains stable without
replacing a higher Latest. Prereleases never become Latest. Historical release
verification uses original signed evidence rather than current policy or
continued Latest status.

## Recovery

Before sealing, repeat qualification with **Re-run all jobs** or a new dispatch.
The seal requires the build identity of the current source, version, run, and
attempt, and it selects test artifacts of the current attempt only. Consumer
and SBOM evidence must match the package bytes being sealed. A partial rerun
that keeps the build or a successful test cell from an earlier attempt therefore
fails sealing; partial reruns are not a general recovery path. Source changes
require a new reviewed candidate. Once sealed, the existing
attestation/publication recovery continues to use the original candidate
artifact IDs and exact bytes.

NuGet cannot atomically publish six package IDs. If a failure occurs after one
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

Pull-request qualification proves the pull-request source only. Hosted qualification proves
the exact hosted source and candidate only. A completed release additionally
requires tag verification, protected approval, short-lived credential exchange,
NuGet signing/readback, and immutable GitHub release readback.

None of these prove an application's authorization, schema customizations,
backup restore, production workload capacity, or direct SQL discipline.

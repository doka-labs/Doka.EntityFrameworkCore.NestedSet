# Release process

This document defines the package and evidence contract. The operator sequence
is in [Release publication](operations/release-publication.md), and decision
authority is in [Release governance](release-governance.md).

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
tests run there. The release candidate independently repeats the solution and release-tooling checks from
its selected `main` commit and additionally verifies isolated package consumers
and SBOMs. Its runner
checks source, dependency locks, and tested binaries between phases; it seals
the candidate only after every check passes. A failed run requires a new output
directory and reruns all checks.

Only the two shipping projects under `src/` commit dependency locks. RC
qualification copies those locks into its owned workspace and verifies that
restore preserves them. Tests, samples, benchmarks, and tools restore their
own graphs without additional committed lockfiles.

| Stage | Required evidence |
| --- | --- |
| `quality` | Offline release-tooling tests, shell syntax, reviewed shipping package locks, Roslyn style/import checks, warning-free Release build |
| `tests` | Docker readiness, every test project, all provider cells, ordinary and optional migrations, coverage artifacts, query plans, and runnable samples |
| `packages` | One pack from tested binaries; exact primary/symbol metadata, assembly, PDB, XML docs, source, dependencies, and archive inventory |
| `consumer` | Independent core-only and EF/SQLite applications restore and execute the exact archives |
| `sbom` | Standalone Microsoft SBOM binary generates and validates one SPDX 2.2 dependency closure per primary package; negative verifier cases run in offline tests |

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

---
id: D-022
status: accepted
date: 2026-10-30
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Shared pull-request and release-candidate qualification jobs, test cells and candidate sealing"
supersedes: []
superseded-by: []
amends: [D-016, D-021]
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-022 -- Qualify releases through the shared CI workflow

## Context and Problem Statement

The release candidate kept its own job matrix, a Python job mode for every
cell, a shared two-hour deadline and a local serial qualification. CI split the
shared MySQL/MariaDB provider project into five filtered cells, but that split
never reached the release matrix. The RC runner also accepted exactly one
unfiltered result per project, so it could not run the same cells.

For 10.1.0, CI finished in about 45 minutes while the release candidate needed
about 78 minutes before publication approval. Its single MySQL/MariaDB cell
alone ran 71.8 minutes; the five CI cells for the same project ran 9 to 24
minutes each. Two matrices had drifted, and the slower one gated releases.

Doka.EntityFrameworkCore.SafeMigrations already calls one reusable quality
workflow from both CI and its release candidate. The owner chose that model.

## Decision Drivers

- One definition of the qualification jobs for pull requests and releases.
- Release duration bounded by the slowest shared cell, not a release-only cell.
- Keep exact inspected packages, consumer and SBOM evidence, release-line
  identity, the sealed candidate format, attestation and publication guards.
- Remove release-only Python orchestration instead of extending it.
- Keep every executable test result complete, passing and free of repeats.

## Considered Options

- Shared quality workflow called by CI and the release candidate
- Release-only matrix with filtered partitions in the RC runner
- Separate MySQL and MariaDB test projects

## Decision Outcome

Chosen option: "Shared quality workflow called by CI and the release candidate",
because one called workflow removes the second matrix and gives the release
candidate exactly the pull-request jobs and their duration.

[The quality workflow](../../.github/workflows/quality-gates.yml) is a
`workflow_call` workflow. [CI](../../.github/workflows/ci.yml) calls it without
inputs and keeps the stable `Repository qualification` gate. The
[release candidate](../../.github/workflows/release-candidate.yml) calls it with
the exact version and the frozen preflight line, then seals, attests and
publishes as before. GitHub reports called jobs as `Qualification / <job>`;
scheduled maintenance verifies those names from the exact workflow sources.

The shared build job restores the reviewed shipping locks into owned copies,
builds the Release solution once with the selected version, packs each shipping
package once and inspects all primary and symbol archives. CI selects the source
version; the release candidate supplies its reviewed version and line. Package
consumers and SBOM generation download that immutable package artifact in every
run. Test and sample cells restore and build their own projects from the same
commit with locked restores. The shared MySQL/MariaDB project keeps its
complementary filtered cells; a contract test proves that the filters partition
the project.

Complete result verification requires a passing, non-skipped result for every
executable test project. Reports from one project may come from several cells,
but no test case may repeat across them. Every promised provider plan family
must exist in at least one cell, because partitioned cells can retain the same
plan name side by side. Coverage must reach all six shipping modules.

The release seal requires the preflight and the complete shared call to
succeed. It downloads the package, consumer, SBOM and current-attempt test
artifacts, restores the build identity of the same run and attempt, rechecks
package hashes, results, plans, coverage, consumers and SBOMs, and assembles the
existing candidate format unchanged. Release notes remain checked in preflight
and again while sealing.

The release-only job graph, TAR transport, runtime inventories, shared 7,200-second
deadline, local serial qualification and `eng/release-candidate.sh` are removed.
Job timeouts and a bounded per-process budget replace the shared deadline.

### Consequences

- Good, because the release candidate runs exactly the pull-request jobs; its qualification follows the slowest shared cell.
- Good, because release-only Python orchestration and its regressions shrink instead of growing for partitions.
- Bad, because test cells no longer execute the packed assemblies' compiled bytes; the binding is the same commit, the locked graph and archive inspection.
- Bad, because pull requests now depend on nuget.org consumer restores and the GitHub-hosted SBOM tool download.
- Bad, because no local complete qualification remains; only hosted runs qualify a candidate.

### Confirmation

- Run `python3 -m unittest discover -s eng/tests -p 'test_*.py'` and require seal, result-partition, plan, producer and workflow-contract regressions to pass. They must reject overlapping partitions, missing projects or plans, a CI identity that tries to seal, a foreign build identity and changed package bytes.
- Run `python3 -m eng.release.qualification --verify-results <directory>` on the downloaded test artifacts of a complete run. The MySQL project's partitions must sum to its full suite, and a missing plan family must fail.
- Pull-request CI must report `Repository qualification` success with every `Qualification / ...` job, including package consumers and SBOMs.
- Before the next release, dispatch the release-candidate workflow with `qualification-only: true`. Expect the shared jobs, a sealed candidate and attestation without the release-only matrix. This hosted confirmation is pending.

## Pros and Cons of the Options

### Shared quality workflow called by CI and the release candidate

- Good, because pull requests and releases cannot drift into different matrices or durations.
- Bad, because each cell builds its own test binaries instead of executing transported canonical bytes.

### Release-only matrix with filtered partitions in the RC runner

- Good, because the release candidate keeps testing the transported canonical compiled bytes.
- Bad, because it grows release-only Python partition logic and keeps two matrices that can drift again.

### Separate MySQL and MariaDB test projects

- Good, because the unchanged one-cell-per-project RC runner would receive two shorter cells.
- Bad, because it restructures the provider test project and still keeps two matrices.

## More Information

[D-016](D-016-release-candidate-evidence-and-publication.md) keeps its chosen
option: qualification still produces one immutable, inspected candidate before
guarded publication. This record replaces how that qualification runs.
[D-021](D-021-release-lines-and-monotonic-publication.md) keeps every
release-line rule; only its retained shared deadline is replaced by job
timeouts and the per-process budget.

### Re-evaluation Triggers

- A shared cell exceeds its job timeout, or a release needs a check that pull-request CI cannot run.
- GitHub changes how called workflows report job names, the caller context or artifact visibility.
- A defect is traced to a difference between per-cell test builds and the packed assemblies.

### Decision History

- 2026-10-30: Decision recorded with status proposed.
- 2026-10-30: Status changed from proposed to accepted.
- 2026-10-30: After the 10.1.0 release candidate waited on its single 71.8-minute MySQL/MariaDB cell, the owner chose the SafeMigrations model, consumer and SBOM checks in every pull request, and removal of the local complete qualification.

### Implementation References

- [Shared quality workflow](../../.github/workflows/quality-gates.yml)
- [Pull-request caller](../../.github/workflows/ci.yml)
- [Release-candidate caller and seal](../../.github/workflows/release-candidate.yml)
- [Shared build and seal runner](../../eng/release/qualification.py)
- [Maintenance producer inventory](../../eng/release/maintenance.py)
- [Seal regressions](../../eng/tests/test_release_seal.py)
- [Workflow contracts](../../eng/tests/test_release_contracts.py)
- [Release contract](../../docs/release-process.md)

### Sources

- [GitHub reusable workflows and the caller context](https://docs.github.com/en/actions/reference/workflows-and-actions/reusable-workflows) (primary source; retrieved 2026-10-30)
- [Release candidate 10.1.0 run](https://github.com/doka-labs/Doka.EntityFrameworkCore.NestedSet/actions/runs/41014625971) (primary source; retrieved 2026-10-30)
- [CI run with five MySQL/MariaDB cells](https://github.com/doka-labs/Doka.EntityFrameworkCore.NestedSet/actions/runs/41009240640) (primary source; retrieved 2026-10-30)

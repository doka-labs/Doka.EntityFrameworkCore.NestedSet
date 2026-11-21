---
id: D-023
status: implemented
date: 2026-11-21
decision-makers: ["Dominic Kalkbrenner"]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Repository-owned engineering contracts, reproducible qualification, and documentation ownership"
supersedes: []
superseded-by: []
amends: [D-001, D-013, D-017, D-022]
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-023 -- Keep engineering contracts reproducible at their owning boundaries

## Context and Problem Statement

The six-package architecture already separates common hierarchy behavior from
native provider integration. Repository maintenance still has inconsistent
constructor and test naming conventions, oversized mixed responsibilities,
implicit speed requirements in functional tests, and independently maintained
copies of mutable documentation. Some execution paths also replace the runner
cancellation token or permit diagnostic failures to replace the original error.

These concerns need one bounded maintenance policy. It must preserve public
APIs, exact hierarchy and rollback guarantees, diagnostic performance evidence,
existing provider support, and the shared parallel qualification process.
Changing code layout must not introduce a new dispatcher, query semantics,
service lifetime, or application-visible unit of work.

## Decision Drivers

- Preserve structural guards, caller ownership, and complete restoration.
- Make ordinary forward work cancellable and necessary cleanup reliable.
- Separate functional correctness and bounded work from machine speed.
- Make source, package graphs, and editor behavior reproducible.
- Keep each contract and decision at one authoritative documentation boundary.
- Remove duplicated maintenance paths without adding a checking framework.

## Considered Options

- Align existing boundaries and qualify each affected contract
- Retain independent local conventions and document their differences

## Decision Outcome

Chosen option: "Align existing boundaries and qualify each affected contract",
because a consistent contract is maintainable only when its actual callers,
fixtures, dependency graph, and recovery behavior are updated together.

Own asynchronous work and callback contracts carry a mandatory final regular
`CancellationToken`. The actual operation token reaches forward work; protected
cleanup and completion of already-started work remain deliberately tokenless.
Framework signatures retain their required shape. A diagnostic write failure
must not replace the original query error or cancellation.

Functional capacity and concurrency tests retain exact data, revision,
exception, work, and resource assertions without private CPU, wall-clock, SQL,
or undo-speed thresholds. Real provider timeout-contract tests keep their
intentional failure conditions. Explicit synchronization and runner cancellation
control ordinary test work; the outer runner retains hang protection.
Observational benchmark contexts also remove omitted command and native undo
timeouts; explicitly chosen limits remain intentional. Their verification and
cleanup belong to the observation lifecycle, not a hidden speed requirement.

Classes and structs use explicit constructors. Positional records remain valid
for data contracts. Responsibility-based files and thematic partials retain
private state, initialization order, and restoration order. Approximately
40 method lines and 400 file lines are review guidelines, not mechanical gates.
Indexable collections use the semantically appropriate loop without forced
materialization. Query expressions retain server-side execution and their
translation, interpretation, visibility, and identity contracts.

Actual tests use `Method_Scenario_ExpectedBehavior`. Renaming includes discovery,
reflection, filters, inherited provider cases, and documentation references;
ordinary sample scenarios share a cancellable in-process entry with the console
application. Dedicated process-lifetime and measurement-isolation tests retain
the process boundary that they actually exercise.

Each solution project records its resolved dependency graph in a committed
lockfile. Normal local restore may update those files; CI enforces the reviewed
graph. Nonshipping transitive pins stay outside shipping package dependencies.
Root source configuration selects the intended package feed, ordinary audit
service failures remain warnings, and explicit security audits continue to
reject missing evidence or vulnerable graphs. This replaces the earlier
shipping-only lock policy without altering the six package contracts.

The existing VSTest runner and Code Coverage 18.12.0 complementary partition
remain. The 2026-11-21 independent and current NestedSet lifetime controls
reproduce the observable unloading failure under 18.12.0 while both product
controls pass uninstrumented; D-013 retains the required boundary. This does
not attribute the original 18.11.0 roots to 18.12.0. A different template's
MTP/Coverlet choice does not establish equivalent
Rider discovery, collectible lifetime, coverage, or result selection. Public and
reflection roles receive conditional IDE annotations from a private build-only
reference; the attributes and package do not become runtime payload.

The team-shared Rider cleanup profile is copied from the actual global
`Built-in: Custom` profile and inspected before use. It does not automatically
convert fields to auto-properties, remove setters, or reformat Markdown.
Adding the profile does not authorize an indiscriminate cleanup run.

Decision metadata `date` identifies the last update and equals the final
history event. Original recording and approval events remain unchanged. The
manual decision index contains a record link and one decision sentence; mutable
metadata remains in the record. The unused JSON index and writing mode are
removed. The optional read-only profile check remains independent of CI/RC.
Public guides own use and failure boundaries, XML documentation owns API
contracts, and ADRs own rationale and alternatives. Development narratives and
duplicated test inventories do not become a second contract store.

### Consequences

- Good, because one consumer-owned contract controls each change and its verification.
- Good, because transient machine load no longer defines functional success.
- Good, because all solution dependency graphs and the effective team profile are reviewable.
- Bad, because changing test names and file ownership requires coordinated reference and discovery checks.
- Bad, because preserving binary, SQL, lifetime, and rollback behavior requires more than a successful compile.

### Confirmation

- Run `dotnet build Doka.EntityFrameworkCore.NestedSet.slnx -c Release --no-restore` and expect zero warnings/errors; compare public API and packed dependency contracts with the approved six-package baseline.
- Compare test discovery through an explicit old/new method mapping and expect unchanged applicable engines, variants, and complete complementary partitions.
- Run positive, rejection, cancellation, cleanup, and rollback controls for the changed boundaries; reject lost original errors, skipped cleanup, or weakened structure guards.
- Check generated/native functional-fixture command settings and application-context isolation on all engines; retain deliberate timeout-error controls.
- Run affected native MySQL/MariaDB/PostgreSQL/SQLite suites and qualify SQL Server on a supported x64 host. An ARM metadata pass is not native SQL Server evidence.
- Run source-matched diagnostic BenchmarkDotNet and allocation controls for changed hotpaths; retain unfavorable measurements and existing deep-tree work bounds without a timing gate.
- Run `bash eng/validate-adrs.sh` and expect valid decision history and manual navigation; verify links and ASCII separately.

Local verification covers the solution build, style/import checks, compatibility,
discovery, package boundaries, and affected native provider controls. Diagnostic
before/after measurements retain mixed and unfavorable observations; they are
not a claim of universal performance equivalence. Native SQL Server on x64,
hosted CI/RC, and Rider profile selection remain separate execution boundaries.

## Pros and Cons of the Options

### Align existing boundaries and qualify each affected contract

- Good, because source, fixtures, documentation, package graphs, and editor behavior evolve together.
- Bad, because the maintenance change spans several consumers and needs an explicit discovery and verification inventory.

### Retain independent local conventions and document their differences

- Good, because it minimizes immediate changes to existing source and test identities.
- Bad, because duplicate policies, implicit timing assumptions, and separate mutable documentation continue to drift.

## More Information

This decision amends the document maintenance rules in D-001 and D-017, test
conventions in D-013, and qualification graph ownership in D-022. Their original
recording, acceptance, and implementation histories are retained. No current
approval is used to reconstruct an earlier approval or a hosted release result.

The change retains the shared parallel qualification workflow, exact candidate
bytes and artifact identities, signed publication evidence, release-line
controls, supported database lines, and existing x64 execution boundary. It
adds no new Python framework or automatic hardware-dependent performance gate.

### Re-evaluation Triggers

- A public consumer or supported compiler exposes a compatibility change from the selected syntax.
- A new collector or runner proves equivalent discovery, lifetime, coverage, and partition behavior.
- A changed hotpath increases native work, retained state, or allocations in a source-matched diagnostic comparison.
- A real consumer requires machine-readable decision navigation beyond the manual index.

### Decision History

- 2026-11-19: Decision recorded with status proposed.
- 2026-11-19: Status changed from proposed to accepted.
- 2026-11-19: The maintainer authorized the complete scoped maintenance plan, including all-project locks, the retained runner/collector boundary, and private build-only IDE annotations. Implementation verification remains open.
- 2026-11-19: Status changed from accepted to implemented.
- 2026-11-19: Local implementation verification completed, including full diagnostic before/after measurements on MariaDB and MySQL. Native x64 SQL Server and hosted qualification are not claimed.
- 2026-11-21: Updated the collector reference to 18.12.0 and recorded the repeated unloading controls under D-013. The observable failure persists with instrumentation; the uninstrumented product controls pass. Complementary partitions, original implementation evidence and re-evaluation requirements remain unchanged; no new GC-root diagnosis or hosted qualification is claimed.

### Implementation References

- [Document profile](MADR-PROFILE.md)
- [Manual decision index](README.md)
- [Package boundaries](D-020-provider-owned-runtime-packages.md)
- [Provider and collector ownership](D-013-provider-owned-integration-test-projects.md)
- [Shared qualification](D-022-shared-ci-and-release-qualification.md)
- [Repository build defaults](../../Directory.Build.props)
- [Central package graph](../../Directory.Packages.props)
- [Contributor guide](../../CONTRIBUTING.md)
- [Performance and resource boundaries](../performance.md)

### Sources

- No external sources; repository evidence only.

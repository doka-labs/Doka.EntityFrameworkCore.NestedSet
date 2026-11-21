# NestedSet development benchmarks

BenchmarkDotNet 0.15.8 measures NestedSet operations in isolated .NET 10 Release
processes. Developers select runs when investigating a change. Results impose
no performance thresholds, CI requirement, release-candidate requirement, or
merge decision. Invalid inputs, failed operations, and missing measurements
produce a nonzero exit code.

Numerical allocation and retained-heap observations formerly asserted by Unit,
provider, and capacity tests follow this same optional boundary. There are no
absolute, per-entry, incremental, or comparative byte pass/fail limits. Normal
build, test, CI, and RC commands do not execute BenchmarkDotNet, including
`Dry`. The ordinary benchmark contract project checks functional workloads and
configuration without statistical execution. Correctness, rollback, ownership,
and live/released lifetime controls remain blocking tests.

Observation contexts remove implicit EF and native command/undo timeouts so a
slow operation or untimed verification remains measurable. Explicit connection
or provider timeouts and later context overrides are preserved. This policy
belongs to the benchmark harness; application defaults remain unchanged, and
an interrupted or failed run is never reported as successful measurement.

Run commands from the repository root. With no arguments, the executable prints
usage and the benchmark catalog without provisioning a database:

```sh
dotnet run --project benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks -c Release
dotnet run --project benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks -c Release -- --list flat
```

Executing options require an explicit native selector, such as `--filter '*'`
for all cases, a method filter, or a category/attribute selector. Without one,
the launcher shows usage and the catalog, then exits with code 2 before creating
resources or artifacts. Help and listing remain available without a selector.

Start with a selected family and dataset:

```sh
dotnet run --project benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks \
  -c Release -- --engine SqliteMemory --nodes 1000 --shape Balanced \
  --filter '*InsertBenchmarks*'

dotnet run --project benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks \
  -c Release -- --engine MariaDb --nodes 100,1000,10000,100000 \
  --shape Wide,Balanced --trees 4 --tracked 0,1000 \
  --filter '*MoveBenchmarks*' --artifacts /tmp/nestedset-move-MariaDb
```

Use `--no-build --no-restore` only after a successful Release build of the current
source. A full Cartesian product can include many independent cases; choose
sizes, shapes, and families deliberately. `--job dry` can check that a selected
scenario executes, but its single observation is not a performance baseline.

## Run settings

| Argument | Effect |
| --- | --- |
| `--engine` | One of `MySql`, `MariaDb`, `PostgreSql`, `SqlServer`, `SqliteMemory`, `SqliteFile`; default `SqliteMemory` |
| `--nodes` | Comma-separated total forest sizes; default `1000`; minimum `10`; examples `100,1000,10000,100000` |
| `--shape` | Comma-separated `Wide`, `Deep`, or `Balanced`; default `Balanced` |
| `--trees` | Comma-separated independent tree counts; default `1`; minimum `1` |
| `--tracked` | Comma-separated counts of unrelated unchanged tracked entities; default `0`; minimum `0` |
| `--artifacts` | Absolute run directory; default a unique directory under `artifacts/benchmarks` |
| `--diagnostics` | Run selected database operations once for untimed SQL and write observations |
| BenchmarkDotNet arguments | Forwarded to BenchmarkDotNet, including `--filter`, `--list`, `--job`, and iteration settings |

Both modes use BenchmarkDotNet's parser, supported option aliases, and response
files such as `@/path/options.rsp` for framework options. Pass provider and dataset
settings above directly to the launcher. Framework filters can match method
names or full cases with parameter values. The framework-selected artifact
directory holds exports, diagnostics, and `provenance.json` together. Each
selected diagnostic method/dataset runs once even when several measurement
jobs select that case.

Engine and shape values are case-insensitive. Basic option validation checks
declared shapes and integer bounds, including the minimum total size of ten.
Before acquiring database resources, the launcher validates each selected
database case against its effective parameters: each tree requires at least
ten nodes for the shared scenario anchors. Unselected dataset combinations do
not undergo this per-tree check. An unmatched selection or diagnostics selecting
no database case fails with code 2 before resources or artifacts are created.
This includes Core and pure signal materialization selections.

`--nodes` is the total size, distributed as evenly as possible across independent
TreeIds. Cross-tree move cases use at least two trees even when `--trees 1` is
requested; the effective count is validated and included in result parameters.
`Wide` makes every non-root node a child of its root, `Deep` creates a chain,
and `Balanced` creates
a binary tree. Construction is iterative with stable keys, parent plans, TreeIds,
and a 1,024-character payload per seeded node.

The parameters select different work: a branch move spans one node in `Wide`,
about half a tree in `Balanced`, and most of a chain in `Deep`. Compare identical
parameters when assessing an optimization. `--tracked` prepares ordinary
unrelated EF entries, rather than tracking an entire hierarchy implicitly.

## Feature families

The project has one source owner and feature folders:

| Folder | Cases |
| --- | --- |
| `Core` | Pure containment and leaf calculations over immutable bounds |
| `Queries` | Tree reads, generic query composition, actual signal/supervisor queries, and pure signal materialization |
| `Insert` | Root, first/last child, before/after sibling insertion, and manual-detection single/subtree/forest saves |
| `Move` | Leaf/branch movement within one tree and root transfer between trees |
| `Delete` | Child promotion and subtree deletion |
| `BulkImport` | Prepared forest import and subtree import sized at `max(10, Nodes / 10)` |
| `Ordering` | Sorted insertion, ordinary rename/save, and manually marked Parent/order saves |
| `Validation` | Quick and full validation of every selected tree |
| `Rebuild` | Repair of damaged bounds, depth, and position with intact adjacency |
| `Measurements/Tracking` | Key/provider comparison, Unicode validation, typed reads, identity refresh, and plan/snapshot construction variants |
| `Measurements/ManagedSave` | Complex capture/restoration, relationship bookkeeping, and hierarchy/save-guard variants |
| `Measurements/Queries` and `Measurements/Providers` | Expression construction, endpoint/member lookup, and real provider-service resolution |
| `Measurements/Consumers` | Actual SignalLab/UserGroups construction, snapshots, materialization, and reduction variants |
| `Measurements/Native` | Real-provider guard, tracked-refresh, and capacity/consumer operation costs with independent result verification |

### Migrated measurement catalog

The linked families expose their exact `Params`/`ParamsSource` values and
setup/workload/cleanup boundaries. These are optional numerical observations;
the corresponding functional and lifetime tests remain required.

**Tracking.**

| Family | Measured boundary |
| --- | --- |
| [BinaryProviderMeasurementBenchmarks](Measurements/Tracking/BinaryProviderMeasurementBenchmarks.cs) | Complete binary provider equality and bounded structural hashing through all four routes. |
| [BulkPlanMeasurementBenchmarks](Measurements/Tracking/BulkPlanMeasurementBenchmarks.cs) | Wide detached plans with sentinel and fully prefilled rollback input values. |
| [ForestPreparationMeasurementBenchmarks](Measurements/Tracking/ForestPreparationMeasurementBenchmarks.cs) | Public forest preparation and its complete pre-I/O rejection recovery as one operation. |
| [IndependentIdentityCaptureMeasurementBenchmarks](Measurements/Tracking/IndependentIdentityCaptureMeasurementBenchmarks.cs) | Independent root identity capture while keeping tracked rows and result slots outside measurement. |
| [InsertionIdentityMeasurementBenchmarks](Measurements/Tracking/InsertionIdentityMeasurementBenchmarks.cs) | Insertion identity capture, refresh, comparison and detach preparation across actual EF storage shapes. |
| [KeyComparerMeasurementBenchmarks](Measurements/Tracking/KeyComparerMeasurementBenchmarks.cs) | Dictionary equality and hashing through typed and deliberately boxed EF comparers. |
| [TrackedIdentityMeasurementBenchmarks](Measurements/Tracking/TrackedIdentityMeasurementBenchmarks.cs) | Stable guard traversal at empty, singleton and candidate-block boundaries. |
| [TrackerSnapshotMeasurementBenchmarks](Measurements/Tracking/TrackerSnapshotMeasurementBenchmarks.cs) | Complete tracker capture for the original twenty-thousand-row, fifteen-scalar unit of work. |
| [TreeIdentityResolutionMeasurementBenchmarks](Measurements/Tracking/TreeIdentityResolutionMeasurementBenchmarks.cs) | Ordered production anchor resolution with independently materialized unrelated rows. |
| [TypedEqualityMeasurementBenchmarks](Measurements/Tracking/TypedEqualityMeasurementBenchmarks.cs) | Cached and framework-reference equality construction with native, lifted and operator-free keys. |
| [TypedPropertyReadMeasurementBenchmarks](Measurements/Tracking/TypedPropertyReadMeasurementBenchmarks.cs) | Live typed parent and structural reads through the exact nullable/converted property model. |
| [TypedProviderMeasurementBenchmarks](Measurements/Tracking/TypedProviderMeasurementBenchmarks.cs) | Typed and erased scalar comparison at actual native and configured converter boundaries. |
| [UnicodeIdentityMeasurementBenchmarks](Measurements/Tracking/UnicodeIdentityMeasurementBenchmarks.cs) | Scalar counting across ASCII, BMP, supplementary and mixed Unicode inputs. |

**ManagedSave.**

| Family | Measured boundary |
| --- | --- |
| [ComplexGraphSnapshotBenchmarks](Measurements/ManagedSave/ComplexGraphSnapshotBenchmarks.cs) | Complex-graph capture and native/scalar recovery using the shared model and oracle. |
| [GuardPlanCaptureBenchmarks](Measurements/ManagedSave/GuardPlanCaptureBenchmarks.cs) | The actual private candidate-plan capture through the existing shared fixture seam. |
| [GuardProbeBenchmarks](Measurements/ManagedSave/GuardProbeBenchmarks.cs) | One real non-mutating hierarchy guard operation with independent correctness checks. |
| [PreparedImportGuardBenchmarks](Measurements/ManagedSave/PreparedImportGuardBenchmarks.cs) | One prepared-import guard at each actual bounded native wave without timing EF setup. |
| [RelationshipCompletionBenchmarks](Measurements/ManagedSave/RelationshipCompletionBenchmarks.cs) | Actual no-relationship completion across every original producer representation. |
| [RelationshipSnapshotBenchmarks](Measurements/ManagedSave/RelationshipSnapshotBenchmarks.cs) | Complete FK capture and endpoint recovery through the actual shared fixture models. |
| [SharingCaptureBenchmarks](Measurements/ManagedSave/SharingCaptureBenchmarks.cs) | Real same-row closure independently from unrelated tracked rows and callback-list reset. |

**Queries.**

| Family | Measured boundary |
| --- | --- |
| [EndpointDagBenchmarks](Measurements/Queries/EndpointDagBenchmarks.cs) | Trusted lexical endpoint resolution for shared source DAGs and named mapping variants. |
| [FixedBindingBenchmarks](Measurements/Queries/FixedBindingBenchmarks.cs) | Fixed and general expression bindings without numeric acceptance thresholds. |
| [FusedBindingBenchmarks](Measurements/Queries/FusedBindingBenchmarks.cs) | SQL construction for separate and fused owned bindings on the same real EF mapping. |
| [MappedMemberReadBenchmarks](Measurements/Queries/MappedMemberReadBenchmarks.cs) | Finalized mapped-member construction beside ordinary expression member construction. |
| [MemberInitializerLookupBenchmarks](Measurements/Queries/MemberInitializerLookupBenchmarks.cs) | Selection from a prepared initializer without visiting its unrelated assignments. |
| [ProjectionReplacementBenchmarks](Measurements/Queries/ProjectionReplacementBenchmarks.cs) | Direct replacement beside the retained synthetic-lambda substitution path. |
| [UnboundMemberBenchmarks](Measurements/Queries/UnboundMemberBenchmarks.cs) | Direct unbound member reuse without evaluating captured or static getters. |
| [VisibilityProofBenchmarks](Measurements/Queries/VisibilityProofBenchmarks.cs) | Visibility proof with one or many references to an existing shared query subtree. |

**Providers.**

| Family | Measured boundary |
| --- | --- |
| [AncestorFallbackCheckBenchmarks](Measurements/Providers/AncestorFallbackCheckBenchmarks.cs) | An already reported informational fallback check with ordinary logging enabled. |
| [ProviderMarkerIdentityBenchmarks](Measurements/Providers/ProviderMarkerIdentityBenchmarks.cs) | The actual Doka source-binding and ordered-join marker identities. |
| [SelectedProviderResolutionBenchmarks](Measurements/Providers/SelectedProviderResolutionBenchmarks.cs) | Initialized selected-adapter resolution on every actual provider callback graph. |

**Consumers.**

| Family | Measured boundary |
| --- | --- |
| [SignalGridConstructionBenchmarks](Measurements/Consumers/SignalGridConstructionBenchmarks.cs) | Complete empty-grid construction at the original 6144-cell presentation grain. |
| [SignalGridStreamingBenchmarks](Measurements/Consumers/SignalGridStreamingBenchmarks.cs) | The original dense streaming and late-failing large sparse materialization workloads. |
| [SignalPathConstructionBenchmarks](Measurements/Consumers/SignalPathConstructionBenchmarks.cs) | The complete direct, two-edge and long-path proof on the real shadow-property model. |
| [SignalReportSnapshotBenchmarks](Measurements/Consumers/SignalReportSnapshotBenchmarks.cs) | Actual request validation and deduplicated same-owner snapshots. |
| [SupervisorChainBookkeepingBenchmarks](Measurements/Consumers/SupervisorChainBookkeepingBenchmarks.cs) | Actual admission counters and retained continuation paths on prepared mutable state. |
| [SupervisorConstructionBenchmarks](Measurements/Consumers/SupervisorConstructionBenchmarks.cs) | Deferred candidate and reduced-resolution construction on the actual sample model. |
| [SupervisorPersonSelectionBenchmarks](Measurements/Consumers/SupervisorPersonSelectionBenchmarks.cs) | Complete winner evidence reuse and canonical distinct-person ranking. |

**Native.**

| Family | Measured boundary |
| --- | --- |
| [AncestorFactConstructionBenchmarks](Measurements/Native/AncestorFactConstructionBenchmarks.cs) | Real indexed fact-selection construction separately from complete read materialization. |
| [AncestorFactReadBenchmarks](Measurements/Native/AncestorFactReadBenchmarks.cs) | Complete native indexed fact reads, including provider reader and occurrence materialization. |
| [CapacityConsumerConstructionBenchmarks](Measurements/Native/CapacityConsumerConstructionBenchmarks.cs) | Actual sample builders using the original capacity-only adapted models and requests. |
| [CleanMutationBoundaryBenchmarks](Measurements/Native/CleanMutationBoundaryBenchmarks.cs) | The real clean-context mutation boundary with an unchanged unrelated tracker. |
| [DensePathConstructionBenchmarks](Measurements/Native/DensePathConstructionBenchmarks.cs) | Every actual dense-path native construction variant without executing the provider. |
| [NativeCompositionConstructionBenchmarks](Measurements/Native/NativeCompositionConstructionBenchmarks.cs) | Complete capacity expression construction on actual provider mappings. |
| [NativeGuardBenchmarks](Measurements/Native/NativeGuardBenchmarks.cs) | Actual tracked-identity admission for native and converted keys on an independently reset catalog. |
| [NativeRefreshBenchmarks](Measurements/Native/NativeRefreshBenchmarks.cs) | Native ordered-save refresh with the exact dense, sparse and adjacent tracker populations. |
| [NativeTrackerWriteBenchmarks](Measurements/Native/NativeTrackerWriteBenchmarks.cs) | The original single-child and 131-root imports with baseline and 20,000-entry trackers. |

Construction families keep their own provider parameter when declared; the
launcher `--engine` option does not replace a family-specific `Engine` parameter.
Likewise, fixed migration profiles retain their original counts and variants;
`--nodes` does not resize a `Case` supplied by that family's `ParamsSource`.
`CapacityConsumerConstructionBenchmarks` builds actual sample expressions over
adapted read-only capacity mappings without database I/O. It retains the
million/ten-million-node request keys and original origin/filter combinations;
it does not allocate or seed that number of physical rows. Native read/write
families instead prepare and verify their physical datasets, which can be large.
List cases and narrow the full case filter before choosing an execution.

List the catalog before selecting a migrated family:

```sh
dotnet run --project benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks -c Release -- --list flat
dotnet run --project benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks -c Release -- \
  --filter '*KeyComparerMeasurementBenchmarks*' --job dry \
  --artifacts /tmp/nestedset-key-comparer-execution
```

The second command is an explicitly requested execution smoke check, not a
performance baseline. Omit `--job dry` for an explicitly selected ordinary
measurement. Both typed and deliberately boxed key-comparer inputs retain the
same present/missing-key correctness checks; their allocation values remain
observations rather than test verdicts. BenchmarkDotNet owns calibration,
warmup, sampling, and exports. The retired handwritten measurement loops and
heap-calibration protocol are not reproduced in a second runner.

For a selected ordinary expression-construction comparison, use the same launcher
without `Dry`:

```sh
dotnet run --project benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks -c Release -- \
  --filter '*ProjectionReplacementBenchmarks*' \
  --artifacts /tmp/nestedset-projection-comparison
```

Native case discovery can select the intended engine without executing a dataset:

```sh
dotnet run --project benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks -c Release -- \
  --engine MariaDb --filter '*AncestorFactReadBenchmarks*' --list flat
```

After selecting an exact case from that listing, omit `--list flat` to execute it;
retain the filter and use a fresh artifact directory. No such run is implicit in
ordinary tests, CI, or release qualification.

`ManualDetectionSaveBenchmarks` measures explicitly marked Parent/order saves;
`ManualDetectionInsertBenchmarks` measures single, ten-node subtree, and ten-node
forest insertion with `AutoDetectChangesEnabled = false`. Fixture reset and
validation are outside timing. The insert family avoids tracking affected
hierarchy nodes, retaining the existing mutation policy; the save family loads
its one candidate. Both retain the selected unrelated tracked entries.
Before/after comparisons keep those entries unchanged so the database work is
identical. Functional tests separately check unmarked payload and rejected raw
hierarchy changes, rather than timing accidental additional writes.

```sh
dotnet run --project benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks \
  -c Release -- --engine MariaDb --nodes 128 --shape Wide --trees 1 \
  --tracked 1000 --filter '*ManualDetection*Benchmarks*' \
  --warmupCount 3 --iterationCount 12 \
  --artifacts artifacts/benchmarks/manual-detection-mariadb
```

The [performance guide](../../docs/performance.md#manual-detection-guard-budgets-2026-11-01)
separates guard allocations from complete native save/insert work. For each new
comparison, retain exact source/binary identity, raw outputs, confidence intervals
and host conditions under ignored `artifacts/benchmarks/`. Observations apply to
the matched fixture, not every later source or a release timing threshold.

`PreparedOwnerImportBenchmarks` compares identical configured-size subtree and two-plan
forest imports with an untracked owner, a loaded empty owner collection, or a
collection containing the complete validated input. All run with manual detection.
Verification checks exact adjacency/tree identities, full hierarchy validity,
retained references, zero automatic detection and a following ordinary save.
Functional contract cases also reject unexecuted or incomplete workloads and
validate the existing `--nodes` selection, including 1,000/2,000/4,000/8,000 inputs.
The prepared case is a new successful contract, so it has no equivalent successful
historical baseline. Use the current owner modes to isolate its additional cost.
Keep SQL-work equivalence separate from timing and one-operation allocation
observations. The [import resource model](../../docs/bulk-import.md#resource-model)
explains the retained plan, lazy membership index and bounded active wave.

The 65/130-node cases exercise wave boundaries. They do not qualify asymptotic
growth. Use the same harness with larger sizes for prepared-owner scaling, keeping
native engines and before/after runs sequential. Guard-only BenchmarkDotNet
workloads are separate from these full imports, which include EF fixup and persistence.
The [save and recovery costs](../../docs/performance.md#prepared-imports-and-recovery-costs)
separate isolated guard scaling from complete native imports, live callback
inspection and recovery authority. Record both favorable and adverse observations,
GC/allocation data and actual background load. Use the existing small and
8,000-node subtree/forest controls without introducing a timing gate.

```sh
dotnet run --project benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks \
  -c Release -- --engine MariaDb --nodes 65,130 --shape Wide --trees 1 \
  --tracked 0 --filter '*PreparedOwnerImportBenchmarks*' \
  --warmupCount 3 --iterationCount 12 \
  --artifacts artifacts/benchmarks/prepared-owner-mariadb
```

The composition families separate expression construction from prepared
relational execution. `CompositionConstructionBenchmarks` and
`CompositionExecutionBenchmarks` use an equivalent application-LINQ reference.
`SignalReportConstructionBenchmarks`, `SignalReportingBenchmarks`,
`UserGroupSelectionBenchmarks`, and `UserGroupChainBenchmarks` invoke the actual
sample builders. These families verify equivalent results, statuses and
occurrence bags outside measurement according to their lifecycles. Supervisor
selection also exposes `Prepared`: setup
builds and asynchronously warms the selected query, measurement executes and
materializes it, and cleanup reconstructs and verifies the complete result.
Its `Api` and `Reference` cases include application reconstruction, so their
different measurement boundaries must remain explicit. The chain reference shares the structural selection
primitive and independently composes the bounded orchestration; it is not an
independent SQL translator. See the [consumer measurement boundaries](../../docs/performance.md#optional-paired-measurements).

The [2026-10-26/27 synthetic comparison](../../docs/qualification/query-performance.md#synthetic-signal-lab-2026-10-26)
retains native read, construction and materializer tables, exact fixed-profile
commands, three MariaDB scale pairs and one prespecified construction repeat.
Original adverse results, allocation increases and residual host load remain
explicit; four original-before deep-work controls and the current-source
counterparts retain independent work and result oracles. The byte limits used
for that dated qualification are historical observations, not current gates.
This short observation profile is separate from the adaptive comparison
protocol below.

SignalLab is a fictional acquisition model with instrument nodes, synthetic
channels and tags, and pulse/sweep/burst event inputs with different physical
fields. Each event retains its identity even when its projected value equals
another event. These fixtures qualify bag, path, owner and reader costs; their
synthetic device rules are not NestedSet domain behavior.

`SignalReportConstructionBenchmarks` builds an unopened SQLite model. Its
construction observations remain SQLite observations even when a native
`--engine` is selected elsewhere. `SignalReportingBenchmarks` executes complete
reads on the actual selected engine. Pure materialization has no database.

Use method/type globs such as `--filter '*QueryBenchmarks.Ancestors*'` or
`--filter '*Ordering*'`. The Ordering filter selects both the insertion and
tracked-save classes. Core cases use only `Nodes`; database cases use all four
dataset parameters. A rename permutes existing siblings in `Wide` and
`Balanced`; `Deep` has no sibling group and measures payload plus coordinated
save work. The tracked-save fixture uses the existing `NestedSetDbContext`
integration for ordinary `SaveChangesAsync`. The
[performance guide](../../docs/performance.md) explains the
library's cost model and existing structural regression contracts.

`SignalReportingBenchmarks` measures complete read-only reports over one prepared
dataset. Its `IrrelevantChoices` parameter selects `0` or `1000` valid catalog
choices that belong to a requested field but are not selected values. Both
cases retain the same selected anchors, metrics, sources and expected report
cells; the extra choices expose unnecessary catalog transport rather than
adding measurement rows. This family fixes `Trees` to `3` and `Tracked` to `0`
for the actual sample's isolation controls. `SignalReportConstructionBenchmarks`
separately measures deferred query construction without database I/O.

`SignalMaterializationBenchmarks` isolates the actual sample's asynchronous catalog
validation and grid construction from EF and database work. Its 24 conditions
combine `Cells` (`24` or `6144`), `Density` (`Empty`, `Sparse`, `Dense`),
`SuspendFirstRead`, and `InterleavedSources`. Sparse input contains two totals;
dense input contains every selected group. The optional suspension yields once
per reader, including an empty reader; it is a deliberate scheduling control,
not a simulation of database latency. Generic forest settings do not change
these fixed scalar fixtures.

```sh
dotnet run --project benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks \
  -c Release -- --filter '*SignalMaterializationBenchmarks*' \
  --outliers DontRemove --artifacts /tmp/nestedset-signal-materialization
```

The historical [six-block CPU/GC/ThreadPool qualification](../../docs/qualification/query-performance.md#historical-report-construction-and-materialization)
retains original/previous/current comparisons, native reports, caller/retention
controls, all adverse observations and precision limits. Its three-cohort runs
used the recorded private provider overlay without changing pins for that
measurement. The current declared Doka 10.7.0/SafeMigrations 10.7.3 graph has
its own [qualification boundary](../../docs/qualification/provider-migrations.md);
the earlier measurements are not reassigned to the newer published binaries.
They also predate the independent SignalLab event model. The commands and
current source links below start new comparisons, not a reconstruction of
those frozen predecessor cohorts.

Global setup checks an independent complete cell oracle. Each measured call
uses the real two asynchronous consumers and updates bounded reader lifecycle
counters. Only its latest result is retained. Global cleanup verifies that
result and aggregate row, EOF, disposal, and suspension counts; intermediate
timed arrays are not individually checked. The public benchmark returns the
actual array as `object` because the sample's cell type remains internal; this
adds no array copy or wrapper. `MemoryDiagnoser` and `ThreadingDiagnoser` report
managed allocation and completed work items/monitor contention respectively.
They do not establish host load, database memory, or exact CPU ownership.

## Providers and resource ownership

MySQL and MariaDB use the Doka EF provider. PostgreSQL, SQL Server, and SQLite
use their actual EF providers. Selected server database cases require Docker
and use the shared digest-pinned [image source](../../docker/database-images.Dockerfile).
The launcher owns one database container with limits of two CPUs and 2 GiB,
including startup, version detection, and
disposal. Container management runs outside the measured child process.
Profiles accept no application database credentials; pooling and ambient
enlistment are disabled for server connections.

The optional [developer Compose environment](../../docker/README.md) uses the
same vendor bases for manual access. Benchmark runs retain their own containers
and never connect to its persistent databases.

Selections containing no database family acquire no database owner, even when
a server engine is configured. This includes the pure signal materialization
family. Their provenance records `database: null`; the configured engine remains
an input setting rather than an observed database.

SQLite memory and file modes are distinct scenarios. The in-memory connection
remains open for its fixture lifetime; file mode creates an owned temporary
database and removes it and its journal files after the run. Embedded SQLite
has no database container limit. Run providers sequentially and avoid competing
database tests when collecting comparable results.

## Measurement boundaries

Families that reset their database state per iteration run one fully awaited
operation with invocation count and unroll factor fixed to one. Their default
.NET 10 throughput job uses three warmup iterations and twelve target
iterations. Core calculations, expression construction, pure signal materialization
and the read-only `SignalReportingBenchmarks` family use BenchmarkDotNet's normal throughput
calibration. Framework job and iteration overrides are supported while
preserving one-operation iterations for reset-dependent families, Release
builds, and separate processes.

Reset-dependent database families restore their initial state and tracker
before each operation. Result verification then checks valid remaining trees
and the feature's expected effect. Rebuild deliberately damages derived
coordinates before each repair. Setup and verification also surround
BenchmarkDotNet's extra allocation diagnostic workload, so repeated insert,
move, delete, and repair observations perform the same work. These iteration
boundaries remain unchanged for the other query and consumer families.

For reset-dependent families, global setup synchronously creates the provider
configuration and fixture; database creation and seeding occur during iteration
preparation. Global cleanup and database workloads remain asynchronous.
BenchmarkDotNet 0.15.8 generates iteration hooks as `System.Action` without the
`AwaitHelper` wrapping used for asynchronous global hooks and workloads. A
Task-returning iteration hook cannot bind to that action, so the void iteration
wrappers fully complete asynchronous helpers before returning.

For `SignalReportingBenchmarks`, asynchronous global setup creates and seeds the
database once per benchmark case. There are no iteration setup or cleanup
hooks, so the framework can calibrate multiple fully awaited report reads per
iteration. Global cleanup checks both the measured result and a fresh API
report against the explicit-LINQ reference, requires an empty tracker and
disposes the context even when verification fails. Its verification reads are
outside measurement.

The signal `Api` invokes the actual `SignalReportBuilder.ReadAsync`. Each complete
read includes request snapshot and validation, the shared read snapshot, one
batched catalog reader, catalog validation, one aggregate reader and scalar
grid construction. `Reference` snapshots the request inside every measured
invocation and performs the same catalog, transaction and result work around
its explicit-LINQ aggregation. The setup snapshot is only a verification
input; it does not replace measured reference validation. The
[complete signal read qualification](../../docs/qualification/query-performance.md#historical-report-construction-and-materialization)
records the comparison boundaries and evidence.

For a matched SignalLab construction and complete-read comparison, keep native
pilot, warmup and actual-iteration calibration enabled. This explicit profile
selects twelve construction cases and eight complete-read cases:

```sh
dotnet run --project benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks \
  -c Release --no-build --no-restore -- \
  --engine SqliteMemory --nodes 40 --shape Wide --trees 1 --tracked 0 \
  --filter '*SignalReportConstructionBenchmarks*' '*SignalReportingBenchmarks*' \
  --launchCount 3 \
  --minWarmupCount 20 --maxWarmupCount 80 \
  --minIterationCount 20 --maxIterationCount 100 \
  --outliers DontRemove \
  --artifacts /tmp/nestedset-signal-comparison-baseline
```

Use a separate artifact directory for the matching changed revision. The signal
reader still uses its own three-tree fixture; generic `--trees 1` does not
override that family parameter. These settings do not change the default
three-warmup/twelve-target, single-invocation job for mutation and other
reset-dependent families. Do not apply this adaptive profile to those families.

For a revision comparison on an interactive host, the later
[query-template reuse qualification](../../docs/qualification/query-performance.md#historical-report-construction-and-materialization)
alternates frozen revisions in `A B B A | B A A B` blocks, using
`--launchCount 1 --keepFiles` for each block and the same adaptive settings.
It retains all twenty cases per block, raw chronological observations and
the generated child bundles logged by BDN. Compare equally weighted process
launches; source fingerprints alone do not identify the executed binaries.
This is a manual evidence protocol, not another benchmark framework or gate.

Leave `--warmupCount`, `--iterationCount`, `--invocationCount` and
`--iterationTime` unset for this comparison. A fixed count bypasses its adaptive
stage, while an explicit iteration time changes pilot selection. The native
automatic pilot retains its default 500 ms minimum iteration time; actual
measurement retains its default 2% relative confidence-margin target or stops
at the configured ceiling. `--outliers DontRemove` retains slow samples in the
distribution. No runtime tiering or PGO settings are disabled.

Adaptive warmup is a heuristic, not proof of stationarity. Inspect raw
chronological warmup and actual values separately for each process launch,
normalized by their operation counts, before pooling launches. Preserve the
first/last-part trends, ceiling hits, between-launch differences and full raw
outputs. If drift or spread prevents a clear comparison, retain and report
that uncertainty; repeat both revisions with matching settings rather than
discarding inconvenient samples. This is an observational investigation
protocol, not an automated performance gate. The tagged
[pilot](https://github.com/dotnet/BenchmarkDotNet/blob/v0.15.8/src/BenchmarkDotNet/Engines/EnginePilotStage.cs),
[warmup](https://github.com/dotnet/BenchmarkDotNet/blob/v0.15.8/src/BenchmarkDotNet/Engines/EngineWarmupStage.cs)
[actual-stage](https://github.com/dotnet/BenchmarkDotNet/blob/v0.15.8/src/BenchmarkDotNet/Engines/EngineActualStage.cs)
and [engine-default](https://github.com/dotnet/BenchmarkDotNet/blob/v0.15.8/src/BenchmarkDotNet/Engines/EngineResolver.cs)
implementations define those native boundaries.

Database/container startup, schema, seeding, tracker preparation, fixture
verification and disposal lie outside timed operations. Materialization,
application input and validation performed inside an operation, provider work,
and asynchronous continuations belong to that operation. Timed consumer
contexts do not register the optional command observer.

The tagged 0.15.8 engine takes its managed allocation snapshots after iteration
setup and before cleanup. On .NET 10 it uses process-wide allocated-byte counts,
including asynchronous continuations on other threads. Background client
allocations may also contribute. Prepared objects still affect heap and GC
state. Allocated bytes and GC activity are not live heap, retained heap, peak
process memory, or database-server memory. The generic diagnostic documentation
contains broader warnings and older thread-counting descriptions; the pinned
engine and GC sources support these specific boundaries. See
[D-015 and its primary sources](../../docs/decisions/D-015-benchmarkdotnet-development-measurements.md).

## Untimed command diagnostics

Run the same scenario and effect checks with operation-only instrumentation.
Untimed diagnostics support both Debug and Release, including breakpoints in
Rider. Actual benchmark measurements require Release and reject a Debug launcher
before acquiring resources or writing artifacts:

```sh
dotnet run --project benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks \
  -c Release -- --diagnostics --engine PostgreSql --nodes 10000 \
  --shape Balanced --trees 4 --filter '*InsertBenchmarks*' \
  --artifacts /tmp/nestedset-insert-PostgreSql-diagnostics
```

`command-diagnostics.json` records all completed EF SQL commands. Its
`HierarchyUpdates` and `AffectedRows` fields have a narrower boundary: only
completed NonQuery commands whose SQL starts with `UPDATE` and names
`BenchmarkNodes` contribute, using the provider-reported row count. Result-
producing payload updates observed through `ReaderExecuted`, including
`UPDATE ... RETURNING` or `UPDATE ... OUTPUT`, count as commands but do not
contribute to those UPDATE/row fields. These fields therefore describe selected
NonQuery UPDATE work rather than the complete set of hierarchy writes.

Counters reset after preparation and are captured before verification queries.
Lock SQL counts as commands; transaction API calls do not. Lock-table updates
and deletes are excluded from the UPDATE/row fields. Repeated qualifying
updates to the same row count repeatedly. Provider matched/affected rows do
not measure physical disk writes or WAL bytes. The diagnostic run contains no
timer, memory sampler, sample statistics, or command/row acceptance budget.
Core methods are absent because they issue no database commands.

## Exports and comparisons

BenchmarkDotNet exports GitHub Markdown summaries, full JSON including detailed
measurements, and raw measurement CSV in the selected run directory. The
credential-free `provenance.json` preserves `source` as the initial commit,
dirty state and SHA256 working-source fingerprint. At completion, `endSource`
records the final identity and `sourcesChangedDuringRun` reports whether the
two snapshots differ. The initial identity is not overwritten by the final
snapshot. The sidecar also records SDK, runtime, packages, CPU, OS and
architecture; database version and image; container resources; dataset
settings; and actual job settings, report success and workload measurement
counts. The toolchain field records an explicitly configured toolchain name;
it is null when BenchmarkDotNet selects its default internally. The framework's
build log remains the evidence for that selection. Diagnostics have their own
JSON plus the same environment provenance.

Compare revisions on the same controlled host with the same datasets, runtime,
provider, images, resource limits, and jobs. Preserve raw outputs with source
identity; inspect spread and repeat paired runs before attributing a change to
the code. If `sourcesChangedDuringRun` is true, repeat the comparison with a
stable source state. Hardware or configuration changes start a different
comparison series.
Earlier handwritten-runner observations are historical evidence with different
measurement boundaries and do not serve as BenchmarkDotNet baselines.

The [benchmark regression project](../../tests/Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests/Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests.csproj)
checks launcher selection, lifecycle, scenarios, diagnostics, and provenance.
It runs in Rider's normal Debug configuration and in Release; Debug additionally
checks that actual measurements are rejected before resource acquisition:

```sh
dotnet run --project tests/Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests -c Debug
dotnet run --project tests/Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests -c Release
```

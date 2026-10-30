# NestedSet development benchmarks

BenchmarkDotNet 0.15.8 measures NestedSet operations in isolated .NET 10 Release
processes. Developers select runs when investigating a change. Results impose
no performance thresholds, CI requirement, release-candidate requirement, or
merge decision. Invalid inputs, failed operations, and missing measurements
produce a nonzero exit code.

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
| `Insert` | Root, first/last child, and before/after sibling insertion |
| `Move` | Leaf/branch movement within one tree and root transfer between trees |
| `Delete` | Child promotion and subtree deletion |
| `BulkImport` | Prepared forest import and subtree import sized at `max(10, Nodes / 10)` |
| `Ordering` | `OrderingBenchmarks.SortedInsert` and `OrderingSaveBenchmarks.RenameAndSave` through `SaveChangesAsync` |
| `Validation` | Quick and full validation of every selected tree |
| `Rebuild` | Repair of damaged bounds, depth, and position with intact adjacency |

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
counterparts pass their unchanged budgets.
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
measurement. The current declared Doka 10.6.3/SafeMigrations 10.6.1 graph has
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

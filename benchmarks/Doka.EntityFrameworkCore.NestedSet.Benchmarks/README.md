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
only Core cases fails with code 2 before resources or artifacts are created.

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
| `Queries` | Complete tree, descendants, filtered descendants, ancestors, children, and explicitly tracked tree reads |
| `Insert` | Root, first/last child, and before/after sibling insertion |
| `Move` | Leaf/branch movement within one tree and root transfer between trees |
| `Delete` | Child promotion and subtree deletion |
| `BulkImport` | Prepared forest import and subtree import sized at `max(10, Nodes / 10)` |
| `Ordering` | `OrderingBenchmarks.SortedInsert` and `OrderingSaveBenchmarks.RenameAndSave` through `SaveChangesAsync` |
| `Validation` | Quick and full validation of every selected tree |
| `Rebuild` | Repair of damaged bounds, depth, and position with intact adjacency |

Use method/type globs such as `--filter '*QueryBenchmarks.Ancestors*'` or
`--filter '*Ordering*'`. The Ordering filter selects both the insertion and
tracked-save classes. Core cases use only `Nodes`; database cases use all four
dataset parameters. A rename permutes existing siblings in `Wide` and
`Balanced`; `Deep` has no sibling group and measures payload plus coordinated
save work. The tracked-save fixture uses the existing `NestedSetDbContext`
integration for ordinary `SaveChangesAsync`. The
[performance guide](../../docs/performance.md) explains the
library's cost model and existing structural regression contracts.

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

Core-only measurements acquire no database owner, even when a server engine
is configured. Their provenance records `database: null`; the configured engine
remains an input setting rather than an observed database.

SQLite memory and file modes are distinct scenarios. The in-memory connection
remains open for its fixture lifetime; file mode creates an owned temporary
database and removes it and its journal files after the run. Embedded SQLite
has no database container limit. Run providers sequentially and avoid competing
database tests when collecting comparable results.

## Measurement boundaries

Database cases run one fully awaited operation per iteration with invocation
count and unroll factor fixed to one. The default .NET 10 throughput job uses
three warmup iterations and twelve target iterations. Core calculations use
BenchmarkDotNet's normal throughput calibration. Framework job and iteration
overrides are supported while preserving one-operation database iterations,
Release builds, and separate processes.

Every database iteration restores its initial state and tracker before the
operation. Result verification then checks valid remaining trees and the
feature's expected effect. Rebuild deliberately damages derived coordinates
before each repair. Setup and verification also surround BenchmarkDotNet's
extra allocation diagnostic workload, so repeated insert, move, delete, and
repair observations perform the same work.

Global setup synchronously creates the provider configuration and fixture;
database creation and seeding occur during iteration preparation. Global cleanup
and database workloads remain asynchronous. BenchmarkDotNet 0.15.8 generates
iteration hooks as `System.Action` without the `AwaitHelper` wrapping used for
asynchronous global hooks and workloads. A Task-returning iteration hook cannot
bind to that action, so the void iteration wrappers fully complete asynchronous
helpers before returning. Database/container startup, schema,
seeding, tracker preparation, validation, and disposal lie outside the timed
operation. Materialization, application input created inside an operation,
provider work, and asynchronous continuations belong to that operation.

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
counts. Diagnostics have their own JSON plus the same environment provenance.

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

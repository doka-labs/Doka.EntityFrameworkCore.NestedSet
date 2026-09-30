---
id: D-015
status: accepted
date: 2026-09-28
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Optional development measurements, provider scenarios, diagnostic boundaries, and reproducible results"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-015 -- Use BenchmarkDotNet for optional development measurements

## Context and Problem Statement

The benchmark executable implemented its own Stopwatch measurements, repeated
samples, process-allocation counters, memory sampling, aggregation, and result
format. Useful provider scenarios and hierarchy checks were coupled to that
measurement infrastructure. The owner rejected the custom measurement approach
and selected BenchmarkDotNet with reproducible real-provider scenarios.

The bounded question is how to investigate NestedSet cost changes with clear
measurement boundaries and source/environment provenance while keeping runs
optional. Statistical measurements and untimed SQL/write observations must not
become CI, release-candidate, or merge acceptance criteria. Existing deterministic
regression-test contracts remain owned by their normal test projects.

## Decision Drivers

- Use established process isolation, warmup, measurement, diagnostics, and exports.
- Measure every operation against the same deterministic starting state.
- Fully await asynchronous database work and reject missing or no-op results.
- Exercise the actual Doka MySQL/MariaDB and other supported EF providers.
- Keep SQL instrumentation and resource management outside timed operations.
- Preserve raw results with source, dataset, database, runtime, and host identity.
- Keep benchmarking explicitly selected and free of performance thresholds.

## Considered Options

- BenchmarkDotNet with controlled scenarios and separate untimed diagnostics
- Maintain the custom observation runner
- Use only structural regression tests and ad hoc application profiling

## Decision Outcome

Chosen option: "BenchmarkDotNet with controlled scenarios and separate untimed diagnostics",
because it provides the measurement lifecycle and exports already needed while
retaining NestedSet-specific data, operation, and effect checks.

Use BenchmarkDotNet 0.15.8, the verified stable version with .NET 10 support,
in one non-packable benchmark project with feature folders for Core, Queries,
Insert, Move, Delete, BulkImport, Ordering/SaveChanges, Validation, and Rebuild.
Dataset parameters select total nodes, wide/deep/balanced topology, independent
trees, and unrelated change-tracker population. MySQL and MariaDB use Doka;
PostgreSQL, SQL Server, SQLite memory, and SQLite file use their actual providers.

The launcher acquires resources only for selected database cases and owns
digest-pinned server containers with two CPU and 2 GiB
limits. Child settings are inherited through the process environment. Container
startup and management are outside measured children. Schema, seeding, tracker
preparation, and effect verification lie outside the operation. Server profiles
accept no application database credentials and disable connection pooling.
Core-only measurements acquire no database owner, even when a server engine
is configured; provenance records `database: null` rather than an unused database
version, image, or resource profile.

The MariaDB profile uses the existing generic `ContainerBuilder` and native
`mariadb` SQL readiness with a two-minute timeout. Testcontainers 4.15.0's
`MySqlBuilder` readiness hardcodes `mysql`, which is absent from the pinned
MariaDB 11.8.9 image. The native check retains the owned image, resource limits,
Doka provider, and connection policy while detecting actual SQL readiness.

Each database iteration executes one awaited operation with invocation count
and unroll factor one. State is restored before every timed or allocation
diagnostic workload and verified afterwards. The default database job uses
.NET 10 throughput measurement with three warmup and twelve target iterations;
Core uses normal throughput calibration. Framework job settings can be adjusted
while preserving Release, process isolation, and restored-state invariants.
`OrderingBenchmarks.SortedInsert` prepares no tracked hierarchy entry;
`OrderingSaveBenchmarks.RenameAndSave` prepares the tracked node it changes.
The `--filter '*Ordering*'` glob selects both classes. A wide or balanced rename
performs a sibling reorder; a deep chain exercises payload/coordinated-save
work because there are no siblings to permute.

Both modes use BenchmarkDotNet's argument parser and case-filter predicates so
framework aliases, response files, parameter selection, and artifact placement
share one source of truth. Diagnostics execute each selected method/dataset
once regardless of its selected measurement jobs.
Untimed diagnostics and their regression tests support Debug and Release so
developers can inspect the same functional path in Rider. The Release-only
launcher guard applies to actual measurements and runs before resource
acquisition or artifact creation; diagnostics never produce timing observations.
Execution requires an explicit native selector, including filters, categories,
or attributes. No arguments show usage and the catalog successfully; executing
options without a selector show the same information and exit with code 2.
Basic shape and integer validation remains independent. Before provisioning,
each selected database case must have at least ten nodes per effective tree,
including the implicit second cross-tree transfer tree. Unselected combinations
do not undergo this per-tree check. No matching cases, or diagnostics matching
only Core cases, also exit with code 2 before resources or artifacts are created.
Help and native listing need no execution selector. The final timed launcher
retains native framework options. The framework's interactive chooser is internal;
explicit selection makes database ownership decidable before execution.

Global setup creates provider configuration and the fixture synchronously
without database I/O; database reset and seeding occur during iteration setup.
Global cleanup and workloads remain asynchronous. The pinned generated template
binds iteration hooks to `System.Action`, and its declarations leave them without
the `AwaitHelper` wrapping used for asynchronous global hooks and workloads.
A Task-returning iteration hook cannot bind to that action. Void iteration
wrappers therefore fully complete asynchronous helpers outside observations.
Its engine takes direct allocation
snapshots after setup and before cleanup. On modern .NET, process-wide allocation
counts include asynchronous continuations and potentially background client
work. Prepared objects still affect the heap and GC behavior. These counts do
not measure retained/peak process memory or database-server memory.

A separate untimed mode invokes the same database feature and effect checks,
capturing all completed EF command counts before verification. Its
`HierarchyUpdates` and `AffectedRows` observations include only completed
NonQuery commands starting with `UPDATE` and naming `BenchmarkNodes`.
Result-producing payload updates observed as readers, including
`UPDATE ... RETURNING`/`UPDATE ... OUTPUT`, count as commands but are absent
from those UPDATE/row fields. They are not complete hierarchy-write or physical
write-amplification measurements. No timer, sampler, budget, or performance
verdict is present. Framework exports include Markdown, full JSON, and raw
measurement CSV.
A credential-free provenance sidecar preserves the initial source commit,
dirty state and working-state fingerprint in `source`. At completion, it adds
`endSource` and sets `sourcesChangedDuringRun` when the two snapshots differ;
the final identity does not overwrite the initial identity. It also records
runtime/SDK/packages, CPU/OS, provider/database image and version, container
limits, datasets, and actual jobs and observation counts. Source changes
require a repeated comparison against stable source before attributing results.

### Consequences

- Good, because standard measurement machinery replaces custom timing and
  statistical code while preserving NestedSet-specific scenarios.
- Good, because deterministic reset plus independent result checks prevents
  repeated mutations or no-op repairs from producing misleading comparisons.
- Good, because SQL diagnostics expose completed command counts and selected
  NonQuery UPDATE/row work without adding instrumentation to the timed process.
- Bad, because the UPDATE/row observations omit result-producing payload
  updates and cannot establish the complete hierarchy write workload.
- Bad, because one-operation database iterations and state restoration require
  substantial untimed database work, and host/database variability remains.
- Bad, because allocated bytes and GC activity do not replace profiling for
  peak process memory, retained objects, database memory, or lock contention.

### Confirmation

- Run `eng/validate-adrs.sh`; expect the complete decision corpus and generated
  indexes to validate, including primary-source and local-link provenance.
- Run `dotnet run --project tests/Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests`
  with both `-c Debug` and `-c Release`;
  expect the scenario, reset/effect, job, lifecycle, diagnostics, and provenance
  regressions to pass, including rejected malformed inputs, failed/canceled
  initialization, and unchanged or damaged operation results.
- Select actual measurements from a Debug launcher; expect code 2 without
  resources or artifacts. Debug diagnostics must still execute and verify the
  selected database operations without timing them.
- Run the documented `--list flat` command in the
  [benchmark operating guide](../../benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks/README.md);
  expect all feature methods without starting a database container.
- Run a selected SQLite database family with `--job dry`; expect a successful
  isolated Release process, one operation per restored iteration, valid effect
  checks, and full JSON, raw CSV, Markdown, and provenance output. This checks
  execution and export rather than qualifying performance.
- Run the same family with `--diagnostics`; expect operation-only command
  observations and no timing, allocation sampler, or acceptance budget.
- Supply `--nodes 9`; expect invalid settings and a nonzero exit before database
  startup. In-process jobs and repeated database invocation must be rejected.
- Select a database case with `--nodes 20 --trees 4`, or a cross-tree move with
  `--nodes 10 --trees 1`; expect code 2 before resources or artifacts. A native
  case filter excluding invalid per-tree combinations must allow a valid case.
- Supply executing options without a native selector, an unmatched selector,
  or a Core-only diagnostic selector; expect code 2 without resources or artifacts.
- Select only Core measurements with `--engine MySql`; expect no database
  acquisition and `database: null` in provenance.
- Repeat a mutation from its restored state; expect the same starting node
  count and actual placement/deletion/repair. An omitted operation or damaged
  final hierarchy must fail result verification.

## Pros and Cons of the Options

### BenchmarkDotNet with controlled scenarios and separate untimed diagnostics

- Good, because isolation, warmup, measurement analysis, diagnostics, and
  detailed exports are maintained by an established framework.
- Good, because scenario parameters and credential-free provenance support
  comparisons of identical work across source revisions.
- Bad, because framework lifecycle constraints require synchronous iteration
  bridges and explicit one-operation jobs for stateful database workloads.

### Maintain the custom observation runner

- Good, because the existing scenarios already combine SQL observations with
  library-specific correctness assertions in one executable.
- Bad, because the project would own measurement isolation, statistics, memory
  boundaries, and result formats that BenchmarkDotNet already provides.

### Use only structural regression tests and ad hoc application profiling

- Good, because deterministic tests already enforce important tree, query,
  write-shape, concurrency, and allocation properties without timing gates.
- Bad, because source comparisons would lack a shared measurement lifecycle,
  representative selectable scenarios, and detailed reusable raw exports.

## More Information

The [performance guide](../performance.md#development-benchmarks) explains how
the optional suite complements the existing cost model and regression contracts.
Historical handwritten observations retain their original measurement scope
and are not BenchmarkDotNet baselines. No new benchmark result is established
by recording this decision or its run procedures.

### Re-evaluation Triggers

- A BenchmarkDotNet/runtime update changes async lifecycle, GC counting,
  iteration boundaries, job behavior, or export contents.
- A new scenario requires different pooling, connection lifetime, payload,
  concurrency, or state-restoration semantics for representative work.
- A proposed CI/RC/merge requirement introduces benchmark thresholds or mandatory
  execution; the owner must explicitly decide that changed contract.
- Repeated comparisons show retained memory or background work materially
  contaminating observations and require additional profiling.

### Decision History

- 2026-09-28: Decision recorded with status proposed.
- 2026-09-28: Status changed from proposed to accepted.
- 2026-09-28: The owner accepted the BenchmarkDotNet proposal and requested its implementation with optional runs and no performance gate.
- 2026-09-29: Clarified selected-case preflight, explicit native execution selection, Core-only resource provenance, synchronous fixture construction, iteration Action binding, and regression ownership under tests.
- 2026-09-29: Limited the Debug launcher rejection to actual measurements; untimed diagnostics and their regression suite also run in Debug for Rider diagnosis.

### Implementation References

- [Benchmark launcher](../../benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks/Program.cs)
- [Measurement configuration](../../benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks/Infrastructure/BenchmarkConfiguration.cs)
- [Iteration lifecycle](../../benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks/Infrastructure/DatabaseBenchmark.cs)
- [Owned provider resources](../../benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks/Infrastructure/BenchmarkEnvironment.cs)
- [Source and environment provenance](../../benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks/Infrastructure/BenchmarkProvenance.cs)
- [Untimed command diagnostics](../../benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks/Diagnostics/BenchmarkDiagnostics.cs)
- [Deterministic forest generator](../../benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks/Scenarios/BenchmarkForest.cs)
- [Benchmark regression project](../../tests/Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests/Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests.csproj)
- [Scenario/effect regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests/BenchmarkScenarioTests.cs)
- [Lifecycle failure/cancellation regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests/BenchmarkLifecycleTests.cs)
- [Native launcher selection regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests/BenchmarkLauncherTests.cs)

### Sources

- [BenchmarkDotNet package and stable release](https://www.nuget.org/packages/BenchmarkDotNet/0.15.8) (primary source; retrieved 2026-09-28)
- [BenchmarkDotNet .NET 10 support](https://benchmarkdotnet.org/changelog/v0.15.0.html) (primary source; retrieved 2026-09-28)
- [Framework Release builds and debugging guidance](https://benchmarkdotnet.org/articles/guides/troubleshooting.html) (primary source; retrieved 2026-09-29)
- [Versioned async declarations](https://github.com/dotnet/BenchmarkDotNet/blob/v0.15.8/src/BenchmarkDotNet/Code/DeclarationsProvider.cs) (primary source; retrieved 2026-09-28)
- [Versioned iteration Action bindings](https://github.com/dotnet/BenchmarkDotNet/blob/v0.15.8/src/BenchmarkDotNet/Templates/BenchmarkType.txt) (primary source; retrieved 2026-09-29)
- [Versioned lifecycle code generation](https://github.com/dotnet/BenchmarkDotNet/blob/v0.15.8/src/BenchmarkDotNet/Code/CodeGenerator.cs) (primary source; retrieved 2026-09-28)
- [Versioned engine timing and GC boundaries](https://github.com/dotnet/BenchmarkDotNet/blob/v0.15.8/src/BenchmarkDotNet/Engines/Engine.cs) (primary source; retrieved 2026-09-28)
- [Versioned process-wide GC counters](https://github.com/dotnet/BenchmarkDotNet/blob/v0.15.8/src/BenchmarkDotNet/Engines/GcStats.cs) (primary source; retrieved 2026-09-28)
- [Versioned one-operation jobs](https://github.com/dotnet/BenchmarkDotNet/blob/v0.15.8/src/BenchmarkDotNet/Jobs/JobExtensions.cs) (primary source; retrieved 2026-09-28)
- [Run strategies and diagnostic caveats](https://benchmarkdotnet.org/articles/guides/choosing-run-strategy.html) (primary source; retrieved 2026-09-28)
- [Memory diagnoser documentation](https://benchmarkdotnet.org/articles/configs/diagnosers.html) (primary source; retrieved 2026-09-28)
- [Versioned generated program](https://github.com/dotnet/BenchmarkDotNet/blob/v0.15.8/src/BenchmarkDotNet/Templates/BenchmarkProgram.txt) (primary source; retrieved 2026-09-28)
- [Versioned child-process executor](https://github.com/dotnet/BenchmarkDotNet/blob/v0.15.8/src/BenchmarkDotNet/Toolchains/Executor.cs) (primary source; retrieved 2026-09-28)
- [Framework exporters](https://benchmarkdotnet.org/articles/configs/exporters.html) (primary source; retrieved 2026-09-28)
- [Versioned full JSON exporter](https://github.com/dotnet/BenchmarkDotNet/blob/v0.15.8/src/BenchmarkDotNet/Exporters/Json/JsonExporter.cs) (primary source; retrieved 2026-09-28)
- [Versioned JSON provenance boundary](https://github.com/dotnet/BenchmarkDotNet/blob/v0.15.8/src/BenchmarkDotNet/Exporters/Json/JsonExporterBase.cs) (primary source; retrieved 2026-09-28)
- [Versioned framework argument parser](https://github.com/dotnet/BenchmarkDotNet/blob/v0.15.8/src/BenchmarkDotNet/ConsoleArguments/ConfigParser.cs) (primary source; retrieved 2026-09-28)
- [Versioned execution selector families](https://github.com/dotnet/BenchmarkDotNet/blob/v0.15.8/src/BenchmarkDotNet/ConsoleArguments/CommandLineOptions.cs) (primary source; retrieved 2026-09-29)
- [Versioned native launcher selection](https://github.com/dotnet/BenchmarkDotNet/blob/v0.15.8/src/BenchmarkDotNet/Running/BenchmarkSwitcher.cs) (primary source; retrieved 2026-09-29)
- [Versioned case glob filter](https://github.com/dotnet/BenchmarkDotNet/blob/v0.15.8/src/BenchmarkDotNet/Filters/GlobFilter.cs) (primary source; retrieved 2026-09-28)
- [Pinned Testcontainers MySQL readiness command](https://github.com/testcontainers/testcontainers-dotnet/blob/37352559c0af87d5c7eeaced1accc2d21e3a4e69/src/Testcontainers.MySql/MySqlBuilder.cs) (primary source; retrieved 2026-09-28)
- [Pinned native Unix readiness strategy](https://github.com/testcontainers/testcontainers-dotnet/blob/37352559c0af87d5c7eeaced1accc2d21e3a4e69/src/Testcontainers/Configurations/WaitStrategies/WaitForContainerUnix.cs) (primary source; retrieved 2026-09-28)

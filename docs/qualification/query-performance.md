# Query and consumer performance qualification

This page summarizes measured construction, complete-reader and materializer
costs. The [performance guide](../performance.md) owns operational guidance and
fixed regression budgets. These observations identify costs and trade-offs;
they do not establish universal speedups, peak memory or hosted release results.

Consumer measurements dated 2026-10-25 or earlier below belong to their frozen
predecessor workloads. They predate SignalLab's independent instrument model,
heterogeneous event formats and synthetic data. Their numeric results, package
identities and observation limits are retained as historical evidence; links to
current regression tests identify corresponding contracts, not a rerun on the
new model. New-model measurements must identify their own source and dataset.

<a id="synthetic-signal-lab-2026-10-26"></a>

## Synthetic SignalLab comparison, 2026-10-26 to 2026-10-27

The independently synthetic event model retains the same technical comparison
axes: scoped instrument trees, overlapping anchors, channels, acquisition ports,
Any/Nearest tags, connection windows and scalar results. Before/after schemas
intentionally differ. These observations qualify matched workloads and cost
boundaries rather than a universal performance improvement.

BenchmarkDotNet 0.15.8 used Release, SDK 10.0.403/runtime 10.0.14 on an Apple M2
Max ARM64. Native runs used Doka 10.6.2 with MariaDB 11.8.9 and MySQL 8.4.13;
each database container had two CPUs and 2 GiB, with pooling disabled. Each case
used two launches, three warmups and five 100 ms target iterations per launch.
Raw JSON/CSV, per-case distributions and source/binary/database provenance are
retained. The fixed short profile below is separate from the longer adaptive
comparison protocol in the benchmark guide.

### Native complete reads

The completed main comparison has 32 paired configurations, or 64 before/after
method rows. Each engine combines Wide/Deep at 100 persisted nodes, Any/Nearest,
zero/1,000 unused catalog choices and both implementations. Three trees, an
empty tracker and complete result verification remain part of each case.

| Engine / implementation | Paired cases | After/before mean range; median paired ratio | Allocated B/read change |
| --- | ---: | ---: | ---: |
| MariaDB API | 8 | 0.703-1.068; 0.996 | -29,556 to +5,086 |
| MariaDB reference | 8 | 0.925-1.057; 0.992 | -3,094 to +12,068 |
| MySQL API | 8 | 0.764-1.064; 0.995 | +1,156 to +7,940 |
| MySQL reference | 8 | 0.906-1.043; 1.000 | -1,040 to +36,856 |

These are ranges across paired case means; the reported median is not a pooled
operation median or a confidence interval. MariaDB Wide/Any's 0.703 ratio has
large before spikes and a broad interval, so it does not establish a speedup.
MySQL's adverse reference Deep/Any/1,000-choice case rises from 741,064 to
777,920 B/read; the API cases also retain their positive allocation deltas.
Host load differed across launches. Neither improved means nor overlapping
intervals prove equivalence, and no cross-engine latency ranking is inferred.

The [independent reference](../../benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks/Queries/Consumers/SignalReportReference.cs)
uses the same tested outer-owner scalar existence form in both native cohorts,
`LongCount(predicate) > 0`, with unchanged predicates. The reference-qualified
before harness remains distinct from the immutable original source used by the
construction/materializer comparison. The
[reference controls](../../tests/Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests/ConsumerReferenceTests.cs)
retain result equivalence and wrong-owner rejection without a timeout increase.

The 2026-10-27 MariaDB scale comparison adds three paired 1,000-node cases,
with Any, zero unused choices, three trees and an empty tracker. It uses the
same reference-qualified before harness as the main native comparison.

| MariaDB scale case | Before ms/read | After ms/read | Ratio | Before/after B/read |
| --- | ---: | ---: | ---: | ---: |
| Wide, API | 1,889.85 | 1,900.90 | 1.006 | 831,888 / 841,456 |
| Wide, reference | 8,617.37 | 8,389.56 | 0.974 | 730,680 / 782,008 |
| Deep, API | 3,355.22 | 3,426.76 | 1.021 | 834,816 / 835,008 |

All three reported interval pairs overlap; this does not prove equivalence.
The reference's additional 51,328 B/read remains an adverse observation. Raw
actual iterations total ten per case, while default outlier handling yields
reported N=8-10. Residual load also differs between cohorts. These three cases
do not qualify a full scale matrix or imply a native MySQL scale result.

### Construction and materialization

Construction completed 12 paired cases, or 24 before/after rows, over 1/4/16
anchors, Any/Nearest and API/reference. It uses an unopened SQLite model;
native engine selection does not make these native construction measurements.
Allocated-byte differences range from -1,554 to +1,095 B/build. Adverse elapsed
observations remain explicit:

| Construction case | Before us/build | After us/build | Ratio | Before/after B/build |
| --- | ---: | ---: | ---: | ---: |
| API, four anchors, Nearest | 190.27 | 309.85 | 1.628 | 65,907 / 66,701 |
| Reference, one anchor, Any | 278.92 | 694.46 | 2.490 | 112,967 / 113,507 |
| Reference, sixteen anchors, Any control | 271.69 | 261.37 | 0.962 | 113,057 / 114,023 |

The first case's reported intervals are about 174-207 versus 185-435 us. The
second has adverse nonoverlapping intervals of about 238-320 versus 388-1,000
us in this loaded cohort; it must not be dismissed as equivalent. The after
construction host load reached approximately 46.5 on twelve logical processors.

One prespecified paired repeat ran on 2026-10-27, counterbalanced after then
original before, with the same fixed settings and this separate result:

| Construction repeat | Before us/build | After us/build | Ratio | Before/after B/build |
| --- | ---: | ---: | ---: | ---: |
| API, four anchors, Nearest | 182.24 | 187.29 | 1.028 | 65,947 / 66,106 |
| Reference, one anchor, Any | 251.91 | 273.72 | 1.087 | 112,829 / 114,824 |
| Reference, sixteen anchors, Any control | 247.27 | 255.69 | 1.034 | 113,755 / 113,764 |

The original 1.628/2.490 penalties did not recur in this one repeat. All three
repeat interval pairs overlap, which establishes neither equivalence nor the
cause of the original differences. Residual one-minute host load was
15.45-20.10 before and 8.44-21.75 after. Each case has ten raw actual iterations;
the one-anchor reference before reports N=9 under default outlier handling.
The original adverse cohort and this repeat are both retained.

The isolated materializer completed 24 paired cases, or 48 before/after rows:
24/6,144 cells, Empty/Sparse/Dense, completed/suspended first reads and ordered/
interleaved acquisition ports. It includes no EF or database execution.

| Reader mode | Paired conditions | After/before mean range; median paired ratio | Allocated B/report change |
| --- | ---: | ---: | ---: |
| Completed first read | 12 | 0.422-1.031; 0.972 | Exactly 0 in every condition |
| Suspended first read | 12 | 0.255-1.733; 0.943 | -1 to +69 |

Across all materializer conditions, the median paired ratio is 0.965. The broad
0.255-1.733 range and external load prevent a general latency improvement
claim. Dense/sparse/empty results, missing versus zero, reader lifecycle and
fixed allocation budgets retain their independent regression checks.

### Deep native work controls

The four original-before 100,001-node controls passed on MySQL and MariaDB,
matching the two variants per engine already passed on the current source.
The [deep work tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Performance/HierarchyCompositionCapacityTests.DeepSignalProofReuse.cs)
retain their original SQL-work, allocation and timeout assertions. MySQL's
exposed iterator work remains within its original linear budgets:

| MySQL control | Before/after sum of iterator output tuples | Unchanged maximum operator tuples | Sum budget |
| --- | ---: | ---: | ---: |
| Any | 1,600,066 / 1,600,062 | 200,002 | 18n |
| Repeated Nearest | 3,802,456 / 3,802,480 | 2,400,024 | 40n |

Both sources retain two commands, 1/96 result rows, 16/50 parameters and zero
tracked entities. MySQL construction is 56,464/59,936 B before versus
56,768/60,336 B after; MariaDB is 56,576/60,152 B versus 56,768/60,715 B.
The modest allocation increases remain explicit. These are work/budget
controls, not paired BenchmarkDotNet latency observations or a complete
ten-million-node qualification.

### Run the same fixed observation profile

Use fresh Release outputs and a separate artifact directory for each source
cohort and engine. The current-source commands are:

```sh
for engine in MariaDb MySql; do
  dotnet run --project benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks \
    -c Release -- --engine "$engine" --nodes 100 --shape Wide,Deep \
    --filter '*SignalReportingBenchmarks*' \
    --warmupCount 3 --iterationCount 5 --iterationTime 100 --launchCount 2 \
    --artifacts "artifacts/benchmarks/signal-current-$engine"
done

for family in SignalReportConstructionBenchmarks SignalMaterializationBenchmarks; do
  dotnet run --project benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks \
    -c Release -- --engine SqliteMemory --nodes 100 --shape Balanced \
    --filter "*$family*" \
    --warmupCount 3 --iterationCount 5 --iterationTime 100 --launchCount 2 \
    --artifacts "artifacts/benchmarks/signal-current-$family"
done

dotnet run --project benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks \
  -c Release -- --engine MariaDb --nodes 1000 --shape Wide,Deep \
  --filter '*SignalReportingBenchmarks.*NearestMetadata: False, IrrelevantChoices: 0, Nodes: 1000, Shape: Wide*' \
    '*SignalReportingBenchmarks.Api*NearestMetadata: False, IrrelevantChoices: 0, Nodes: 1000, Shape: Deep*' \
  --warmupCount 3 --iterationCount 5 --iterationTime 100 --launchCount 2 \
  --artifacts artifacts/benchmarks/signal-current-scale

dotnet run --project benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks \
  -c Release -- --engine SqliteMemory --nodes 100 --shape Balanced \
  --filter '*SignalReportConstructionBenchmarks.Api*Anchors: 4, NearestMetadata: True*' \
    '*SignalReportConstructionBenchmarks.Reference*Anchors: 1, NearestMetadata: False*' \
    '*SignalReportConstructionBenchmarks.Reference*Anchors: 16, NearestMetadata: False*' \
  --warmupCount 3 --iterationCount 5 --iterationTime 100 --launchCount 2 \
  --artifacts artifacts/benchmarks/signal-current-construction-repeat
```

The complete selected regression scope passed 11,048 C# cases. The Unit result
includes 430 required uninstrumented cases and 2,279 cases under the actual
18.11.0 collector. The six completed observation cohorts contain 74 matched
cohort pairs and 148 measured source rows; the repeat revisits three existing
parameter configurations. The named original-before/current-source deep-work
controls also pass with their original budgets; the four original controls
are separate from the 11,048 current-source cases. This local scope is neither
complete ten-million-node nor native SQL Server/hosted RC qualification. No
wall-clock gate, relaxed assertion, timeout increase or new library mechanism
is introduced by these observations.

## Construction and warm-read allocation

Separate construction and execution. A warmed query cache avoids compilation;
it does not remove EF parameter extraction, cache lookup, asynchronous reader
state, provider work, catalog transport or result materialization.

The 2026-10-20 allocation traces covered 65,536 complete diagnostic reads with
zero lost events. Recognized-stack attribution assigned approximately 64-66%
of sampled allocation weight to EF execution/materialization and 26-28% to
parameter extraction. Prominent types included funcletizer `State[]`, boxed
integers and execution-context state. Allocation ticks are coarse sampled
weights, not exact per-phase byte totals; absent inlined frames do not prove
zero library allocation. The predecessor's complete report is profiled separately
below; these mixed-profile percentages must not be substituted for its stacks.

Expression metadata and immutable projections are reused only at exact weak
model or closed syntax identity. Contexts, mutable captures, requests and
result sets are not global cache values. Allocation controls retain foreign
mappings, unevaluated getters, changing captures, converter and shadow-property
paths, callback rollback and independent endpoint aliases.

## Historical complete-report phase attribution

Two 2026-10-25 diagnostic cohorts profile the predecessor's complete `ReadAsync`,
independently from plain timing. MariaDB's earlier externally loaded cohort and
MySQL's later serial-window cohort each contain four managed-thread/runtime
and four allocation/GC profiles. Each trace covers 1,536 operations inside BDN
actual-workload bodies. All sixteen retain zero lost events, no missing required
stacks and no unmatched runtime-suspension boundaries. Different host windows
and instrumentation prevent a direct CPU/latency comparison between engines.

Allocation stacks are assigned once to their closest recognized frame. These
are disjoint sampled allocation-weight shares, not exact phase byte counters:

| Recognized category | Before Any | After Any | Before Nearest | After Nearest |
| --- | ---: | ---: | ---: | ---: |
| EF parameter extraction | 19.72% | 16.29% | 20.85% | 5.57% |
| EF cache key/lookup | 2.25% | 0.83% | 2.39% | 1.28% |
| EF execution/materialization | 54.57% | 57.78% | 63.43% | 71.49% |
| Sample/NestedSet construction | 11.12% | 10.38% | 6.53% | 4.94% |
| Driver/provider/other categories | Remaining share | Remaining share | Remaining share | Remaining share |

The same nearest-frame classification for the later MySQL cohort is:

| Recognized category | Before Any | After Any | Before Nearest | After Nearest |
| --- | --- | --- | --- | --- |
| EF parameter extraction | 19.78% | 11.66% | 19.21% | 5.22% |
| EF cache key and lookup | 2.59% | 0.84% | 2.18% | 1.21% |
| EF execution and materialization | 56.51% | 62.93% | 64.43% | 75.11% |
| Sample and NestedSet query construction | 11.30% | 11.25% | 7.02% | 5.44% |

These are separately captured cohorts; their percentages are not a pooled
cross-engine CPU or allocation comparison. Every stack contributes to one
category, with unresolved runtime/observer weight retained.

The EF execution category also includes command/template processing; it is not
an isolated DTO-materializer measurement. A higher percentage can accompany
lower total allocation. Convert neither these shares nor CPU stack-sample
counts into exact allocated bytes, CPU ownership, physical I/O or peak memory.
GC suspension overlap includes only GC/preparation intervals inside workload
bodies; profiler/other suspension is reported separately. ThreadPool events
and waiting-thread samples are not per-operation CPU timers.

Plain MariaDB observations under external CPU/memory pressure record lower
complete-read allocation ranges: Any 1,039,179-1,041,100 B before versus
830,575-834,162 B after; Nearest 2,970,503-2,974,907 B before versus
1,747,862-1,753,344 B after. These are process-level BDN observations, not a sum
of exact per-phase bytes or a claim about retained memory. The subsequent
serial full-reader comparison and isolated dense materializer control appear
separately below. No
latency speedup is inferred from this externally loaded cohort.

## Matched construction after the parallel test load

A fresh 2026-10-25 A-B-B-A repetition retains five actual builder shapes and
all four process outcomes. The same finalized model/factory warmup, five warmup
iterations, ten target iterations and 4,096 builds per iteration remain; raw
outliers are kept. The source/package identities match the complete-reader
comparison below.

| Shape | Before us/build | After us/build | Before B/build | After B/build |
| --- | --- | --- | --- | --- |
| MariaDb-1-Nearest | 285.462 / 289.786 | 64.088 / 63.228 | 192415 / 192724 | 79196 / 79002 |
| MySql-1-Nearest | 295.094 / 279.963 | 65.434 / 64.167 | 191018 / 190779 | 78913 / 79732 |
| Sqlite-1-Any | 103.265 / 102.059 | 50.467 / 52.749 | 92450 / 92671 | 65765 / 67663 |
| Sqlite-1-Nearest | 136.582 / 136.174 | 54.575 / 50.598 | 114992 / 114140 | 67299 / 67190 |
| Sqlite-16-Nearest | 141.922 / 135.721 | 52.441 / 51.393 | 116016 / 114277 | 66808 / 66181 |

Both after means and allocations are lower for every listed shape in this
series. These are process means, not pooled confidence bounds or portable
thresholds. Some proof construction moves to EF compilation; earlier cold
translation observations include slower MySQL/SQLite cases and must remain
separate from the warm builder. Complete-reader and isolated-materializer
observations below qualify different work boundaries.

## Matched complete reads after the parallel test load

A 2026-10-25 follow-up ran the actual complete report serially on MariaDB and
MySQL after the other user tests ended. It retained the same local packages,
final product identity, 40-node/three-tree/24-cell fixture and complete result
oracle. Each engine ran four A-B-B-A processes, four warmups and twelve target
iterations of 128 reads, without forced GC or removed outliers.
Normal aggregate OS load remained observable; this was not an idle-host claim.
Whole-host busy medians were 38.0%/39.48% for the MariaDB/MySQL plain windows,
including the workload and normal OS/application activity; memory compression
persisted. These host observations are not benchmark CPU ownership.

The table retains both process means for each variant, not a pooled confidence
interval or an elapsed-time release threshold:

| Engine/mode | Before ms/read | After ms/read | Before B/read | After B/read |
| --- | --- | --- | --- | --- |
| MariaDb Any | 7.9474 / 7.9939 | 7.2171 / 7.5772 | 1040735 / 1041100 | 833578 / 830413 |
| MariaDb Nearest | 14.7034 / 14.4099 | 13.0899 / 13.1392 | 2966104 / 2965274 | 1758319 / 1753082 |
| MySql Any | 6.8302 / 7.6362 | 6.3548 / 6.5884 | 1040240 / 1042430 | 832273 / 832117 |
| MySql Nearest | 12.7408 / 12.8980 | 11.2426 / 11.5222 | 2960115 / 2961691 | 1752680 / 1753844 |

In this series both after-process means are lower for each shape and measured
allocation is lower. This observation does not establish universal latency,
CPU ownership, peak/retained memory or a benefit for another source/provider
combination. Raw iterations and the earlier externally loaded observations
remain separate evidence rather than being trimmed or merged. The isolated dense materializer controls and native phase profiles have their
own scopes; full-reader allocation is not exact byte attribution by phase.

## Isolated dense materializer control

The same 2026-10-25 serial window completed four A-B-B-A materializer processes,
with 24/6,144 dense cells and completed/first-read-suspended readers. The real
sample accumulator/grid path is measured without EF query or database work.
Setup and result/lifecycle verification remain outside timing. Each process
uses four warmups, twelve target iterations and 1,024 invocations; no forced
collection or outlier removal is applied.

| Shape | Before us/op | After us/op | Before B/op | After B/op |
| --- | --- | --- | --- | --- |
| 24 completed | 8.490 / 7.855 | 7.821 / 7.811 | 5016 / 5016 | 5016 / 5016 |
| 24 suspended | 19.502 / 18.478 | 22.307 / 20.823 | 5760 / 5728 | 5766 / 5765 |
| 6144 completed | 222.175 / 221.339 | 225.099 / 224.390 | 865683 / 865683 | 865683 / 865683 |
| 6144 suspended | 251.399 / 259.886 | 256.042 / 255.927 | 866466 / 866466 | 866466 / 866467 |

These are both retained process means, not confidence-interval bounds.
Completed allocation is identical; the small suspended path has slightly higher
observed allocation and slower after means. Large completed means are also
slightly higher; other elapsed comparisons are mixed. Individual BDN confidence
intervals overlap, which proves neither equivalence nor absence of regression.
There is no uniform materializer speedup. The current query changes do not
introduce a new materializer algorithm, and full-reader gains must not be
assigned to grid creation from these controls. Sparse/empty behavior and fixed
budgets remain separate semantic/allocation tests.

<a id="historical-report-construction-and-materialization"></a>

## Historical predecessor report construction and materialization (2026-10-23)

That measured consumer read selected catalog evidence and grouped totals
inside one qualified snapshot. It streamed scalar validation/sum indexes and
creates the final result array. The short LINQ example explains usage; the
complete report additionally handled dynamic metadata, temporal edges,
three physical fact sources and rejection-order contracts.

The 2026-10-23 source comparison used runtime/EF 10.0.13 and BenchmarkDotNet
0.15.8. Its local Doka package was `10.6.2-local.20261023.30d6bfd5`, built from
HEAD `30d6bfd508cf0650efb28bbdb3fa3bd089bd34b0`. That source precedes the later
GUID split-mapping fix described in the
[provider record](provider-migrations.md#doka-mapping-compatibility).
Construction made 4,096 actual builds per measured iteration after warmup.
One-anchor Doka Nearest builds used approximately 189-193 KB in the retained
local launches; means were 276.58/276.87 us on MariaDB and 291.13/293.35 us
on MySQL. Construction times remained mixed across cohorts.

The complete-reader fixture had 40 stored nodes, three trees, zero tracked
entities and 24 result cells. Each process used four warmups and twelve
iterations of 128 reads, without forced collection or outlier removal.
The table preserves the two local launch means rather than presenting them
as one statistically established result.

| Engine / owner mode | Elapsed ms/read | Allocated B/read |
| --- | ---: | ---: |
| MariaDB Any | 7.833 / 8.213 | 1,039,776 / 1,040,444 |
| MariaDB Nearest | 13.708 / 14.069 | 2,973,428 / 2,965,687 |
| MySQL Any | 6.891 / 6.942 | 1,040,956 / 1,044,279 |
| MySQL Nearest | 12.263 / 12.372 | 2,983,452 / 2,960,725 |

Those bytes cover the complete reader, not just the 24 output cells or the
NestedSet library. In that historical cohort, MySQL Nearest means improved relative to its older
private provider control, while Any and construction did not improve uniformly.
Results remained mixed on MariaDB. Low aggregate CPU load did not establish
identical single-thread execution conditions or a cause for timing variation.

Isolated materializer comparisons use the actual sample's completed and
suspended readers, independently of EF/database work. Dense allocation savings
were retained, but several completed-reader and complete native elapsed/CPU
means increased. Empty, sparse and dense results and original request order
remain distinct contracts. Retaining 128/256 large diagnostic results changes
live-heap/GC pressure and is not equivalent to an application retaining only
its latest result. Process allocation is not live or peak memory.

## Managed hot paths

The 2026-10-22 measured binary-provider specialization uses complete span
equality and an unboxed hash equivalent to the framework's bounded structural
hash. A 4,096-candidate verification changed from approximately 1.18 MB to zero
verification allocation in its fixed binary fixture. Scalar verification was
already allocation-free and its elapsed observations remained mixed.
Arrays and spans differ from interface collections: the retained indexed
loops avoid measured enumerator allocation for their actual collection types;
this is not a general preference for `for` over `foreach`.

The hot-path controls check equality outside the bounded hash window,
converters, nulls, unexpected provider values, scalar packing boundaries,
63/64/65 block boundaries and late tracker attachments. No complete mutation
or report is claimed allocation-free.

## Expression construction

The 2026-10-23 paired construction experiment retained immutable fixed path
syntax and omitted unused count-branch construction. It rejected a weak
mapped-member preparation cache because its complete-report gain did not
justify added retained state and admission branches. The selected source
preserved exact factory validation, independent aliases and deep-tree work.
The historical after-cohort average saved about 3,675 B per MariaDB Nearest
build and 5,143.5 B per MySQL build; individual elapsed means remained mixed.
Complete native reads did not show a consistent latency benefit.

An attempted new absolute allocation assertion was rejected after unchanged
assemblies produced a later higher observation. The retained test checks the
specific shared-literal mechanism and old-assembly rejection instead; existing
whole-builder budgets were not increased. Such mechanism checks supplement,
rather than replace, native work and complete-consumer allocation controls.

## EF ownership experiment

An isolated official EF 10.0.13 source experiment corrected ownership of rented
funcletizer state arrays after successful visits, exceptions and revisits.
It retained the existing expression/parameter/exception contracts. Ownership
controls passed 27/27 with the correction versus 15 failures with unchanged
source. In a separate four-case actual signal comparison, observed complete-read
allocation fell by approximately 11-15%; elapsed and CPU effects remained mixed.

This is framework investigation evidence, not a permanent-memory-leak claim,
a shipped EF fix or a NestedSet/Doka feature. No experimental EF assembly is a
shipping dependency. The corrected funcletizer source SHA-256 was
`ad089b6968fe923558988c1994b20435818661e3aff78aeae9a07f6b6f2f87bb`;
the original was
`31b4911a4367ad232749d92f7827a3a9fc5de13f70b25089fac12b0da61f5f9a`.
A local upstream issue draft now preserves the exact historical source, patch,
probe and red/green reports. Its proposed portable wrapper has not yet been
executed, and no issue has been submitted or accepted. It does not establish
current EF-main behavior or introduce an experimental shipping dependency.

## Run a new source-matched comparison

The repository's existing BenchmarkDotNet fixtures call the real sample and
verify complete results outside timing. Run before/after cohorts serially with
identical packages, data, runtime, settings and source-frozen Release outputs.
Use separate artifact directories and retain unfavorable iterations.
These commands execute current SignalLab sources; they do not reconstruct the
historical cohorts above. The construction family initializes an unopened
SQLite model, regardless of a native engine argument. Only the complete-reader
family below executes the selected native engine.

```sh
dotnet run --project benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks \
  -c Release -- --engine MariaDb --nodes 40 --shape Wide --trees 3 --tracked 0 \
  --filter '*SignalReportingBenchmarks*' --outliers DontRemove \
  --artifacts artifacts/benchmarks/signal-read-mariadb

dotnet run --project benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks \
  -c Release -- --nodes 40 --shape Wide --trees 3 --tracked 0 \
  --filter '*SignalReportConstructionBenchmarks*' --outliers DontRemove \
  --artifacts artifacts/benchmarks/signal-build-sqlite

dotnet run --project benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks \
  -c Release -- --filter '*SignalMaterializationBenchmarks*' --outliers DontRemove \
  --artifacts artifacts/benchmarks/signal-materialization
```

Repeat each native family on both engines. Record package/DLL hashes, actual
server versions, row/result counts and native plans. CPU sampling, GC events,
ThreadPool events and allocation ticks are diagnostic observations, not exact
per-phase counters or causal explanations. Keep instrumented runs separate
from plain BenchmarkDotNet timing replicates. No benchmark timing is a CI or
release threshold; structural work, semantic and fixed allocation guards remain
in the ordinary C# tests.

# Performance and capacity

Nested sets exchange write amplification for fast ordered hierarchy reads. The
package keeps client work iterative and bounded, while database range updates
remain proportional to the affected coordinate suffix in the worst case.

The maintainability, UserGroups, and earlier dated comparisons below identify
their frozen sources and package graphs.
They predate the [D-020 provider-package split](decisions/D-020-provider-owned-runtime-packages.md),
including removal of optional reflected Doka binding. Their measured values
and test counts remain historical evidence; they do not establish fresh
performance or qualification of the adapter architecture.

Consumer observations dated 2026-10-25 or earlier also predate the independent
SignalLab instrument model and its heterogeneous acquisition events. Retained
numbers describe the frozen predecessor source and workload. Current sample or
regression links preserve the technical contract; they do not turn those
numbers into measurements of the redesigned model.

The [2026-10-26/27 SignalLab comparison](qualification/query-performance.md#synthetic-signal-lab-2026-10-26)
records completed native complete-read, SQLite construction and isolated
materializer cohorts, three MariaDB 1,000-node pairs and one prespecified
construction repeat for the redesigned acquisition model. Timings are mixed;
original adverse observations and additional allocations remain visible. The
four original-before deep-work controls and their current-source counterparts
pass with unchanged work and allocation budgets.

## Allocation measurement and Unicode counting

The Unit suite's deterministic allocation budgets run without coverage instrumentation. The
suite marks its isolated allocation collections and standalone budget controls
with `Category=Allocation`. CI and RC run that partition once without a
collector and the complementary Unit cases once with coverage.
The same partition includes the registration's collectible-adapter lifetime
controls. The [D-013 instrumentation decision](decisions/D-013-provider-owned-integration-test-projects.md)
retains Code Coverage 18.11.0 with this mandatory partition. Lifetime assertions
run against uninstrumented application behavior, with a deliberately retained
registration as a negative control. Actual xUnit metadata tests protect this
classification; no lifetime assertion is relaxed or skipped.
Both partitions are mandatory; result validation rejects missing partitions,
duplicate case identities, failed cases, and skipped cases. Other provider
suites retain their ordinary coverage run. Budgets are unchanged, and no
product behavior depends on whether a collector is present.

### Coverage collector 18.11.0 and unloadability

The 2026-10-26 diagnosis used a framework-only collectible assembly, without
NestedSet, EF Core or Doka dependencies. On macOS 26.5.2 ARM64 with SDK 10.0.403
and runtime 10.0.14, the statically instrumented module remained alive after
ten full GC/finalizer cycles. Capture happened before any assertion or
`WeakReference.Target` observation. The actual 18.11.0 collector injected two
independent unloading blockers:

- A strong root through AppDomain `ProcessExit`/`DomainUnload`, an event delegate,
  its `_methodBase`, and the collectible `LoaderAllocator` reaches the module's
  static test object.
- A background thread remains inside the injected `PeriodicFlushLoop`. This is
  an active method-frame blocker, distinct from the observed strong heap root.

| Independent diagnostic control | Object and load context after GC |
| --- | --- |
| No collector | Collected |
| No collector, intentionally retained object | Alive, as expected |
| Code Coverage 18.10.1 | Collected |
| Code Coverage 18.11.0 | Alive |
| 18.11.0, only injected exit-event registrations removed | Alive; flush frame remains |
| 18.11.0, only injected flush worker stopped | Alive; event-root path remains |
| 18.11.0, both diagnostic interventions | Collected |

The interventions are causal controls in that isolated probe, not product
changes or an application mitigation. The source-only reproduction and raw
root/stack evidence are retained in the maintainer review archive as
`finding-codecoverage-18.11.0-collectible-gcroot-2026-10-26.md` and its companion
directory. Its portable copy reproduced the old collector, unchanged new
collector and combined-control results. These observations do not establish
Windows/Linux or dynamic-instrumentation behavior, vendor confirmation, or a
new hosted release qualification.

The owner retained 18.11.0 because the required uninstrumented partition tests
ordinary NestedSet ownership without collector-created references. This does
not repair the collector or qualify unloading in an instrumented host. The two
GC-lifetime controls contribute no coverage; the complementary Unit cases
continue to supply instrumented execution evidence.

To run the existing Unit partition contract from a fresh Release build:

```bash
dotnet build tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests -c Release
dotnet test tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests -c Release --no-build --no-restore \
  --filter "Category=Allocation" --logger "trx;LogFileName=allocations.trx" \
  --results-directory artifacts/unit-instrumentation-review
dotnet test tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests -c Release --no-build --no-restore \
  --filter "Category!=Allocation" --settings eng/coverage.runsettings --collect:"Code Coverage" \
  --logger "trx;LogFileName=coverage.trx" --results-directory artifacts/unit-instrumentation-review
python3 -m eng.release.qualification --verify-results artifacts/unit-instrumentation-review \
  --project Doka.EntityFrameworkCore.NestedSet.Unit.Tests
```

Use a new results directory for each comparison. Both invocations and result
validation must succeed. Do not merge the invocations into one instrumented
run or treat a lifetime failure as a skip. Commands here describe the existing
procedure; this documentation change does not report a new test run.

Re-evaluate the decision when the collector, executed runtime, platform or
instrumentation mode changes; repeat the independent plain/18.10.1/candidate
controls and the opposing NestedSet lifetime tests. Close the collector finding
only after the instrumented module collects without removing events or stopping
its worker. A requirement for instrumented unloading, additional retention
affecting other tests, or changed partition selection/result validation also
reopens the decision under D-013. Qualify a replacement or the 18.10.1 fallback
if the required contract cannot be preserved; do not weaken assertions.

### Unicode counting contract

The common Unicode counter reuses one immutable framework `SearchValues<char>`
for the complete surrogate range. Its bounded initialization runs once; warmed
counting allocates no buffers or boxes in Debug or Release. Exhaustive code-unit
and malformed-surrogate controls retain its scalar-counting contract.

On 2026-10-28, an ordinary uninstrumented Debug run reproduced 96 bytes per
call in the generic `IndexOfAnyInRange<char>` helper. This was therefore not
limited to coverage instrumentation. The cached search preserves the exact
zero-byte warm contract in both build configurations. The historical comparison
below retains its latency tradeoff; this change does not establish a universal
speedup. Allocation budgets still run without profiler instrumentation.

### Unicode counting and allocation measurement, 2026-10-26

The final source passed 428 uninstrumented Unit cases and 2,250 complementary
cases with the actual Code Coverage 18.10.1 collector. Their 2,678 test IDs are
disjoint, and the reports contain executed lines in all six shipping modules.
The complete native suites passed 4,307 MySQL/MariaDB, 2,007 PostgreSQL and
2,039 SQLite cases. Migration and optional SafeMigrations suites passed 203
and 96 cases. The 2,081 native SQL Server cases excluded by the ARM policy
still require x64 qualification; local metadata cases do not replace it.

An isolated BenchmarkDotNet 0.15.8 comparison used SDK 10.0.403 and runtime
10.0.14 on an Apple M2 Max, after the native suites finished. Both cohorts use
the same inputs and scalar validation. `Cached` denotes the preceding cached
predicate; `Generic` denotes the restored framework range search.

| Count input | Cached before | Generic after | Warm allocation, both |
| --- | ---: | ---: | ---: |
| ASCII, 8 code units | 1.302 ns | 1.609 ns | 0 B |
| ASCII, 256 code units | 21.141 ns | 13.022 ns | 0 B |
| Late surrogate pair, 256 code units | 21.291 ns | 13.464 ns | 0 B |
| Mixed short input | 5.387 ns | 5.887 ns | 0 B |
| Short paired input | 9.195 ns | 8.741 ns | 0 B |
| BMP, 8 code units, batched control | 1.881 ns | 1.595 ns | 0 B |

The ordinary scan/count comparison completed 24 cases with three warmups and
five measured iterations per case. The original single-call BMP timing was
reduced to zero by harness-overhead subtraction and is excluded from latency
claims. Its paired control uses 512 calls per invocation, identical input read
through changing array slots, and two fresh launches. That row includes the
same loop/array cost in both cohorts and is not directly comparable with the
single-call rows. Independent 10,000-call allocation controls report zero
bytes for both counters on every shape, and 196,608 first-surrogate comparisons
agree.

The longer identities save approximately 8 ns. Short ASCII and mixed inputs
are approximately 0.3-0.5 ns slower; the paired-input timing intervals overlap.
This restores simpler production code without its extra predicate factory,
not a universal latency improvement or a benchmark gate. The removed factory
allocated 2,104 B on its first measured access and retained a compact predicate;
that was a one-time cost, not an allocation on every read. These isolated
counter results do not establish whole-query or provider-split performance.

<a id="provider-split-allocation-controls-2026-10-26"></a>

## Provider split allocation controls, 2026-10-26 (historical cached-search run)

The [scoped adapter binding](../src/Doka.EntityFrameworkCore.NestedSet/Infrastructure/NestedSetProviderRegistration.cs)
validates completed native ownership once per context scope and returns the
existing immutable adapter. Actual warmed `Resolve` controls allocate zero
bytes across 10,000 calls on all five engine profiles, including after a full
GC. They initialize actual provider service graphs without database I/O.

Under Code Coverage 18.10.1, the generic surrogate-range search
allocated 96 B per counter call; its uninstrumented execution allocated zero.
The allocation trace recorded 91 warmed-budget samples of boxed `char` objects,
24 B each, at that helper, with zero lost events. The temporary typed
cached-search implementation required zero warm bytes in both modes. It
covered the complete `D800..DFFF` range and preserved valid-pair counting and
malformed-surrogate rejection. On the measured .NET 10.0.13 ARM64 runtime,
initialization allocated 2,104 B, including a temporary `bool[2048]`, and
retained a compact 32 B predicate; its character input buffer was stack-allocated,
not a retained 4 KiB array.

The separate uninstrumented BenchmarkDotNet comparison completed 24 scan/count
cases. Complete counting of the two inputs with 256 UTF-16 code units cost
about 7-9 ns more; the short ASCII/BMP inputs improved by about 0.3 ns. Other
timings were mixed. This was an allocation/latency tradeoff, not a universal speedup. That
Unit suite passed 2,676 cases normally and with coverage, with executed
lines in all six shipping modules. The full MySQL/MariaDB suite passed 4,307
cases before the isolated counter refinement; 70 affected native Unicode and
compiled-model cases passed afterward. This local evidence establishes neither
hosted release qualification nor native SQL Server execution on ARM. The
[regression map](regression-coverage.md#provider-specific-identity-lengths)
links the metadata, exhaustive range, rejection, and zero-allocation controls.
These counts describe the cached-search source and earlier SDK, not the later
measurement split or restored generic counter. The cached predicate and its
initialization buffer are absent from the current implementation.

## Maintainability comparison on published packages

The 2026-10-25 comparison uses published Doka 10.6.2 and EF 10.0.13 on both
frozen product cohorts. All 68 plain BenchmarkDotNet records completed across
17 construction, signal, UserGroups and materialization cases. Two fresh processes
per cohort run in before/after/after/before order; diagnostic timings are
excluded. The percentage changes below describe means of process means, not a
pooled confidence interval, significance test, equivalence proof or hardware gate.

| Complete group read | Before process means | After process means | Descriptive change |
| --- | --- | --- | ---: |
| MariaDB, one origin | 6.146 / 6.086 ms | 5.958 / 6.222 ms | -0.43% |
| MariaDB, 16 origins | 6.567 / 6.935 ms | 9.169 / 6.911 ms | +19.09% |
| MySQL, one origin | 5.406 / 5.409 ms | 5.679 / 5.869 ms | +6.78% |
| MySQL, 16 origins | 9.063 / 9.218 ms | 9.122 / 9.287 ms | +0.70% |

The MariaDB 16-origin processes vary substantially; both MySQL one-origin
after processes are slower. Construction and signal timings are also mixed.
These observations establish neither a general speedup nor regression-free
performance. Group reads allocate approximately 20-35 KB more per read in
these cases, about 3-6%; the fixed per-read overhead is not an improvement.
The materializer's synchronous 24-cell case stays at 5,016 B/op and its
6,144-cell case at 865,683 B/op; suspended large reads stay near 866,466 B/op.
Small suspended reads vary slightly across processes.

Residual background activity remains material: observed mean host CPU idle
ranges from 45.89% to 59.38%, with substantial memory compression. Whole-child
CPU includes setup, warmup, verification and observation; it is not isolated
benchmark-body CPU. The 24-cell materializer has MinIterationTime warnings
with roughly 8-24 ms actual iterations, so its latency evidence is weaker.
All 68 separate diagnostic captures were parsed with complete actual-body
intervals and zero lost events. CPU sample weights include waiting threads;
allocation ticks are sampled, so neither gives exact CPU duration or bytes per
type. The synchronous 24-cell profile has only 166/172 before/after actual-body
thread samples and is excluded from CPU root-cause claims. Diagnostic timings
remain outside the 68 plain observations; the profiles establish neither a
universal causal explanation nor exhausted optimization.

The [published runtime record](qualification/provider-migrations.md#published-runtime-qualification-2026-10-25)
separately records all nine test projects and explicit native SQL Server ARM
exclusions. The existing [benchmark guide](../benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks/README.md)
defines consumers, lifecycle checks, diagnosers and result-retention boundaries.
Earlier dated measurements below retain their original provider and source
identities; these fresh comparisons do not relabel historical results.

The separately instrumented setup comparison retains two database containers
plus one Ryuk helper before and after. The immutable key-comparison fixture
adds one database, 16 table-creation and 28 index-creation statements per engine;
the complete two-engine filters use four versus six databases. Seed frequency
and actual payload stay unchanged at 108 saves and 30,592 readback rows, and the
server-plan DDL multiset is identical. The 114 explicit readback observations
are excluded from ordinary command counts. EF completed commands and native
Execute activities are different layers and are not added together. These
functional setup scopes overlap and include observer/logging overhead, so
their durations establish no setup speedup or additive throughput result.

## UserGroups allocation follow-up, 2026-10-26

This comparison starts from the current post-maintainability source, not the
older before cohort above. Both cohorts use published Doka 10.6.2, EF 10.0.13,
SDK 10.0.401 and BenchmarkDotNet 0.15.8. All 32 plain records completed: two
fresh processes per cohort in before/after/after/before order, across actual
construction and complete `ResolveAsync` reads on MariaDB and MySQL. Each pair
below lists individual process means or bytes per operation. Diagnostic timings
are excluded; changes in means of process means are descriptive, not a pooled
confidence interval or proof of equivalence.

| Complete read, origins / metadata fields | Before B/op | After B/op | Before ms/op | After ms/op |
| --- | ---: | ---: | ---: | ---: |
| MariaDB, 1 / 1 | 611808 / 610318 | 585765 / 586140 | 6.239 / 6.200 | 6.125 / 6.229 |
| MariaDB, 16 / 4 | 617286 / 618310 | 593042 / 590018 | 7.320 / 7.627 | 7.583 / 7.179 |
| MySQL, 1 / 1 | 610842 / 610461 | 585591 / 586653 | 5.801 / 5.641 | 5.934 / 5.603 |
| MySQL, 16 / 4 | 617282 / 615733 | 588340 / 589005 | 9.135 / 9.297 | 9.237 / 9.286 |

Complete reads allocate about 24.5-27.8 KB less per operation in these controls,
4.02-4.51%. Their latency remains mixed: descriptive mean changes are -0.69%
and -1.24% on MariaDB, +0.84% and +0.49% on MySQL. Individual process confidence
intervals overlap across cohorts; this is no general read speedup or proof of
unchanged latency.

| Construction only, origins / metadata fields | Before B/op | After B/op | Before us/op | After us/op |
| --- | ---: | ---: | ---: | ---: |
| MariaDB, 1 / 1 | 242448 / 240961 | 217989 / 217299 | 212.616 / 210.761 | 176.135 / 199.259 |
| MariaDB, 16 / 4 | 243800 / 244001 | 218668 / 218728 | 211.712 / 214.292 | 188.529 / 187.941 |
| MySQL, 1 / 1 | 241817 / 242974 | 219046 / 218282 | 211.170 / 206.268 | 192.289 / 192.809 |
| MySQL, 16 / 4 | 244852 / 244745 | 218896 / 219266 | 222.570 / 207.386 | 192.661 / 185.339 |

Construction allocates 23.7-25.7 KB less per call, 9.79-10.51%, with descriptive
mean-time changes of -11.33% / -11.63% on MariaDB and -7.75% / -12.08% on MySQL.
These warm observations exclude initialization. The sample retains twelve
immutable, request-free expression trees per loaded sample assembly; standard
EF options gain one extension-reference snapshot array and weak-table
bookkeeping per options instance. Exact retained bytes and cold latency were
not measured. Per-call savings do not establish a smaller cold footprint.

The [shared projections](../samples/UserGroups/Queries/Supervisors/SupervisorQueries.Projections.cs)
capture no context, model, request, query or provider extension node. The
then-current provider-owner reader (`Providers/NestedSetDokaQueryFeatures.cs`,
replaced by the [typed adapter](../src/Doka.EntityFrameworkCore.NestedSet.MySql/Queries/MySqlNestedSetProvider.Queries.cs))
snapshots only the exact standard options map's membership. Every lookup still
reads current provider metadata and validates actual assembly identity; custom
options and subclasses retain the uncached path. A warm snapshot hit also
avoids recreating generic type metadata after a full GC. No coordinate cache
was added: the fresh warm profiles provided no positive named CPU/allocation
sample for the two coordinate methods. Absence of samples does not prove
nonexecution or exclude inlining.

The unchanged 224 KiB [construction budget](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Samples/UserGroups/SupervisorProjectionCacheTests.cs)
rejects the original implementation in all eight engine/request variants and
passes the new implementation. Capture rejection, reversed deferred request
execution and changed authorization are separately tested. Two complete Unit
runs pass 2,691 cases each; the native MariaDB/MySQL selections pass all 500
UserGroups cases and 36 work/deep-consumer controls, including 100,001 nodes
at depth 100,000. These functional timings are not comparative benchmarks.

All 32 separate final diagnostic traces contain complete actual-operation
intervals and zero lost events: 40,960 construction or 1,536 read operations per
trace. Named provider-owner CPU samples fall from 58-75 to zero in construction
and 7-12 to zero in reads; sampled allocation stacks also show less repeated
reflection/expression work. Zero named samples do not prove nonexecution, and
sample weights are neither exact CPU durations nor bytes per type. GC overlap
decreases in the construction controls but is mixed for complete reads;
ThreadPool waits and adjustment events remain observed separately. Host mean
CPU idle is 51.75-53.20% during plain runs and 51.68-51.93% during profiles,
with substantial memory compression. These conditions and one process per
diagnostic cohort/mode limit causal and general latency conclusions.

## Cost model

Let `n` be the node count of one TreeId and `s` the size of a selected subtree.

| Operation | Dominant work |
| --- | --- |
| Tree read | Tree-local `(Scope?, TreeId, Left)` range; examined rows and downstream work depend on the plan and application filters |
| Children | Indexed `(Scope?, TreeId, ParentId, Position)` range |
| Ancestors | A valid tree returns at most `Depth` strict ancestors; containment visits and temporary work depend on the actual plan |
| Insert | Opens a two-coordinate gap and may update a tree suffix |
| Move | Stages `s` rows and shifts source and destination ranges |
| Delete with promotion | Deletes one node, reparents direct children, closes one gap |
| Delete subtree/tree | Deletes `s` rows and closes or retires the selected tree |
| Full validation/rebuild | Linear structural projection and iterative traversal |
| Bulk import | Linear compact plan plus bounded database batches |

TPT, table splitting, and entity splitting require a mapping-aware deletion
path because EF cannot translate their deletes as one `ExecuteDelete` statement.
It reads at most 128 primary keys per batch and caps each batch at 900 key
parameters, then deletes dependent table fragments before principal fragments.
Client memory is bounded by the batch; database round trips scale with the
number of batches and mapped tables. A hierarchy mapped to one table, including
inline `OwnsOne` values, retains the one-statement delete path when it is the
principal of that table.

Depth alone does not determine read cost, and a shallow wide tree can still
have expensive early inserts. Inspect the actual data shape and provider plan.

SQLite repair and bulk-finalization writes expose each bounded key batch to
the unique NodeKey or `(Scope, NodeKey)` index. A correlated unique-key lookup
checks exact TreeId membership inside the same UPDATE. Without collected statistics,
combining TreeId and key predicates directly had selected a complete-tree
range scan for every batch in the million-row qualification. The current
shape requires neither `ANALYZE` nor an implementation-named index hint.
Regression plans cover scoped composite keys, converted unscoped keys, and
an adversarial physical table name that could shadow the membership alias.
See [SQLite query planning](https://sqlite.org/optoverview.html) and
[EXPLAIN QUERY PLAN](https://sqlite.org/eqp.html).

PostgreSQL's generated tree paths use `TreeId IS NOT NULL`; the conventional
nullable self-FK path uses `Parent IS NOT NULL` and has a root companion.
The required TreeId keeps every row in the structural indexes. Tree equality
queries can use them, while FK principal checks cannot substitute a scope-only
structural scan for their Scope/NodeKey lookup. During a large uncommitted
import, automatic analysis can observe no committed rows and invalidate the
cached RI plan, even though the importing transaction sees its growing rows.
The measured failure scanned over 400,000 rows per new child. Filtering only
the Parent index left a second structural scan eligible. The final model
excludes these generated paths from that principal check without an FK,
planner-setting, or maintenance bypass.

The local diagnostic on 2026-10-04 observed actual post-autoanalyze RI statements
with 500,032 and 965,376 rows visible to the importing transaction. Both used
the composite alternate key with both index conditions, one returned row,
five total buffer hits, and no filter scan. Public tree-query plans also use
the filtered structural path for native and converted TreeIds. These are
observations of the qualified fixture, not a universal optimizer or latency
guarantee. An application-owned unfiltered index can change plan eligibility.
Separate native `PREPARE` tests force a generic plan and verify the server's
generic/custom counters. Parent equality uses the dependent partial index,
root selection uses its companion, and principal-only lookup excludes both.
Error and cancellation controls restore the session mode and remove only the
test-owned prepared statement. These plan controls do not replace the actual
million-node import or change runtime planner settings.
Review the [migration and index ownership contract](migrations.md#postgresql-index-predicates)
and PostgreSQL's [partial-index rules](https://www.postgresql.org/docs/17/indexes-partial.html)
and [plan invalidation](https://www.postgresql.org/docs/17/sql-prepare.html).

## Scope and TreeId design

Scope is an optional application partition such as tenant or volume. TreeId is
the concurrency and coordinate partition inside that Scope. Two trees with
the same node count may both use bounds `1..2 * nodeCount` while locking
independent registry rows on server databases.

Choose one TreeId for nodes that must support atomic cross-branch moves. Split
independent write-heavy hierarchies into different TreeIds. A permanently hot,
very large tree remains a serialized nested-set write partition; an adjacency,
path, or database-native hierarchy model can be a better choice for that
workload.

## Query discipline

- Start from `InTree(treeId).Nodes`, `TreeContaining(nodeKey)`,
  `SubtreeOf(nodeKey)`, `ChildrenOf(nodeKey)`, or `AncestorsOf(nodeKey)`.
- Add translatable payload predicates before materialization.
- Keep the built-in no-tracking behavior for read-only work.
- Project only required columns for large results.
- Order a complete hierarchy by Left; a payload-only sort flattens levels.
- Add application indexes for frequent payload filters that the structural
  indexes cannot cover.
- Inspect actual provider plans after schema or distribution changes.

For qualified unconverted integral and `Guid` node keys, changed-key resolution
and save refresh use the provider's collection-parameter transport. Parent-change
dependency planning uses bounded ordinal endpoint batches so source and target
anchors remain exactly correlated under database equality. The lock and affected
refresh plans contain one request per distinct Scope/TreeId pair in the provider
representation; registry locking still resolves database-equal aliases under
provider semantics. Save refresh combines affected Scope and TreeId intervals in
bounded queries. A NodeKey that is an EF key by itself is correlated by key
alone. A scope-qualified NodeKey is correlated with its projected Scope when
that Scope also has native integral or `Guid` equality, so identical NodeKeys
in different scopes remain distinct without one refresh query per scope. Other
Scope types resolve aliases in SQL first, then issue one native key query per
matching tracked scope. Converted keys and custom equality mappings use bounded
ordinal rowsets whose branches match Scope and NodeKey together; the native path
is never assumed from the CLR type alone. Tracked-row capture for Parent changes
applies the locked-tree filter once outside each rowset, so one statement holds
a key batch plus at most 64 locked trees instead of their product.

The explicit mutation guard uses the same qualified collection transport for
unchanged tracked candidates with native NodeKeys. It groups captured keys by
Scope and applies that Scope's native SQL predicate with the key collection.
This preserves duplicate NodeKeys in separate scopes without a Scope/NodeKey
cross product. Each group's unique key array is built once before requested
tree batching; Doka's equality join receives a deduplicated collection.
At most 64 complete requested trees appear in each probe; a
large native key collection remains one collection parameter rather than one
scalar parameter per key. The scalar fallback packs at most 768 candidates
before grouping by provider Scope. One predicate contains those qualified
groups, so many small Scopes do not cause one query each. Candidate predicates
are built once and reused across requested-tree batches. Definite affected
provider representations still
fail before any database command; unresolved collation aliases and externally
moved rows are checked after all requested locks are held. After the awaited
membership probes complete, the executor rechecks the mutation context and
captured tracker identities before structural writes.

`EF.Parameter(...)` explicitly selects collection parameter translation where
the provider supports it. EF Core 10's default parameterized `Contains`
behavior is not itself evidence of a single collection parameter. Doka's
qualified path uses a `JSON_TABLE` equality join for indexed lookup; other
accepted providers use their verified collection membership translation.
Capability checks include mapped conversion and supported store transport,
not only the key's CLR type. Tests inspect generated commands, parameter
count, late affected membership, and the scalar-pack/request-batch boundaries.
See [EF Core 10 collection translation](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew#improved-translation-for-parameterized-collection)
and [collection parameter modes](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.parametertranslationmode?view=efcore-10.0).

Other bounded key collections explicitly use `EF.MultipleParameters` where
the finalized property has a qualified native CLR type without a converter.
The same metadata-aware policy applies to every multi-value consumer,
including sorted bulk refresh. This per-query selection remains
stable when the application chooses a global Constant or Parameter collection
mode. Converted scalar keys retain balanced, mapped equality predicates even
when their model CLR type is an ordinary integer or string. This conservative
qualification policy does not assert that EF cannot translate converted IN.
Single-value and explicitly scalar rowset filters do not create collections.
The scalar guard
allows at most 1,536 padded candidate and Scope parameters plus 128 parameters
for 64 complete requested identities, for a bound of 1,664. This stays below
EF's SQL Server threshold; regressions inspect the actual parameterized commands
rather than relying on the unpadded collection length.

Scalar probes select the constant `1` with a one-row limit and use `0` to
represent no match. The complete indexed predicates and 768-candidate pack
remain intact. EF's `Any` shape introduced an outer `EXISTS` expression whose
height was counted across SQLite's nested resolution scopes; a large converted
pack exceeded the default depth limit. The direct first-row shape avoids that
extra scope while materializing at most one scalar value. Native collection
probes retain their qualified membership path. Deployment-specific lower
SQLite limits require separate qualification; see
[SQLite expression-depth limits](https://www.sqlite.org/limits.html#max_expr_depth)
and the pinned translation and resolver sources in
[D-012](decisions/D-012-typed-tree-runtime.md#sources).

For `n` uncertain tracked entries, `r` requested trees, and `g` captured Scope
groups, the native membership path issues at most
`g * ceiling(r / 64)` guard queries; an unscoped hierarchy has one group.
This is the source algorithm's bound for
the membership probes, excluding registry locking and operation queries; it
is not a measured latency claim.
Initial definite-identity capture uses a lazy hash set of complete
`(Scope, TreeId)` pairs with provider-representation comparers resolved once.
Its expected client work is `O(n + r)`; identity size, converter cost, and hash
collisions can still affect that work. This does not certify CPU throughput.
The scalar fallback issues at most
`ceiling(n / 768) * ceiling(r / 64)` membership queries, including when a pack
contains many small Scopes. Scope-qualified predicates preserve exact key
ownership. Improving transport does not remove the tracked snapshot cost.

Automatic reorder resolves the changed identities of each scope in batches of
at most 64 keys, so a save that changes thousands of ordered nodes stays below
provider parameter limits, including for converted keys. Under
`AllowManualPlacement`, changed nodes are placed one at a time in criterion
order. When one tree's changed nodes span several batches, that order is read
again from the database: with one key filter up to 64 keys, otherwise by
streaming the tree's keys in criterion order.

Each sibling-permutation write batch also exposes its minimum and maximum
`Right` as a native range predicate. The exact disjoint intervals remain in
the filter, so intervening subtrees are unchanged. The envelope allows a
bounded index seek when an optimizer otherwise treats the interval disjunction
as a residual predicate. The SQLite regression checks actual generated writes
both before and after `ANALYZE`; it does not depend on a CPU-time threshold.
See [SQLite range and OR planning](https://www.sqlite.org/optoverview.html).

Wide multi-tree lock plans use one JSON rowset parameter for global database
ordering on every provider. This avoids scalar-parameter ceilings without
sorting independent chunks in the client. The ordering query is one statement;
actual registry lock acquisition still executes one or two statements per
TreeId. A forest with many independent trees therefore has command count
proportional to its TreeId count even though ordering uses one parameter.
Forest payload writes share 64-node batches across trees; they do not issue an
EF save for each root. The 131-root regression requires exactly three saves.

After acquiring those locks, a coordinated save rechecks each changed node's
current TreeId before its payload write. This bounded projection prevents an
order change from writing in a tree reached by a concurrent move but absent
from the lock plan. Parent moves in the save use the existing transaction and
registry locks; only their current endpoints are resolved after earlier moves
in the same save. No nested mutation savepoint or second lock acquisition is
required for each Parent change.

## Mutation discipline

- Keep hierarchy and related domain writes in one short application
  transaction.
- Use `InsertSubtreeAsync` or `InsertForestAsync` when the complete topology is
  already available.
- Keep network calls and user interaction outside the transaction.
- Do not track a complete tree before issuing a set-based mutation.
- Scale concurrent writers across independent TreeIds.
- Treat sustained lock wait on one TreeId as a data-model signal.

## Memory behavior

Ordinary reads remain deferred and do not materialize results inside the
library. Single-node mutations retain metadata and small structural values;
range shifts execute in the database.

Structural Parent values carry an inline presence bit and typed NodeKey, so
nullable value-type parents do not require one boxed value per structural
row. NodeKey, TreeId, and Scope remain typed through facade scalar dispatch,
feature state, registry requests, and active single/bulk staging. Generic
dispatch and nullable join/setter delegates are closed during cached
initialization, rather than rebuilding those specializations for each
operation or node. A Scope binding allocates its private typed carrier once;
operations read it by casting the carrier reference. EF metadata assignment,
sparse original rollback values, heterogeneous refresh projections, and
registry ordering remain explicit boundaries where a non-generic framework
contract may require boxing. Refresh projections include application-defined
concurrency properties alongside structural roles; their bounded mixed result
rows do not become an alternate retained identity model.
Managed-save groups create typed entity wrappers once from the heterogeneous
pending entries. Multi-role reads use the public typed property-values getter;
one live `CurrentValues` wrapper serves multiple reads for an entry and is
retained by each guard candidate for late verification. It reflects current
changes rather than replacing the independently owned identity snapshots.
A cached per-key Parent adapter reads the mapped nullable representation
without per-row reflection or boxing through an object-valued property getter.
CLR value access can avoid fresh boxing, while the values wrapper still
allocates, including constructor complex-property lists in EF 10.0.12, and
shadow storage may already contain boxed values. A single-role read retains
the public typed property-entry getter to avoid that values-wrapper cost for
one value. This is not a zero-allocation claim for every EF mapping or proof
of lower total guard allocation. Candidate state stays inline rather
than retaining one additional candidate object per tracked entry.
The private buffer uses 64-record blocks so growth copies only its small
directory, not all earlier records and property-entry references. It uses no
pool or global cache that could retain application entries after the guard.
An `InTree` query and its tree facade share one owned TreeId snapshot. This
synchronous handoff does not require another clone. Store and lock requests
keep independent snapshots because their operation lifetimes and ownership
differ. The context-scoped store reuses its immutable Scope/TreeId query;
no cached query escapes that context's lifetime.
The warmed public-value regression reuses one wrapper for integer and converted
Parent reads plus Left, Right, Depth, and Position across 100,000 iterations.
It allows at most 4,096 allocated bytes for that fixed CLR-backed fixture,
including an observable accumulated result. The budget detects per-read
boxing or wrapper recreation; it does not qualify every shadow or property-bag
representation as allocation-free.
`TypedProviderComparerTests` isolates warm native identity comparison: 20,000
iterations for integer, Guid, and present nullable-parent representations
allow at most 16,384 allocated bytes. This fixed budget detects per-pair
boxing without measuring CPU speed or database latency.
Configured converted values keep their necessary converter boundary. The
converted comparer regression compares typed equality with direct Matches
using the same converter work, allowing at most 16,384 additional bytes across
20,000 warmed iterations. It checks provider aliases and hashes as well as
allocation, so skipping the converter cannot satisfy the contract.

An explicit mutation with uncertain tracked hierarchy entries retains their
independent key, Scope, and TreeId snapshots until native membership and late
tracker checks finish. Its client memory is proportional to that tracked set;
collection transport reduces database commands, not the required snapshot
ownership. Native transport adds one deduplicated key array per captured Scope
group, constructed once and reused across request batches. This storage is
linear in captured keys rather than multiplied by the requested tree count.
The guard does not retain entry wrappers for unrelated entity types or another
full entity graph. Converted key fallback keeps each query expression bounded
even when the captured tracked set is large.

The native 20,000-candidate regression caps additional allocation at 2,600
bytes per candidate after subtracting a warm baseline operation. This covers
EF guard work and provider collection transport, including serialization;
it measures allocated bytes, not retained heap, CPU speed, or database latency.
The baseline difference is normalized by the 20,000 captured candidates.

Local targeted qualification on 2026-09-27 at 13:18-13:19 CEST produced the following
additional bytes per candidate, rounded down as reported by the test:

| Engine | `int` NodeKey | `Guid` NodeKey |
| --- | ---: | ---: |
| MySQL | 1,614 | 2,178 |
| MariaDB | 1,587 | 2,125 |
| PostgreSQL | 1,534 | 1,806 |
| SQL Server | 1,595 | 2,191 |
| SQLite | 1,587 | 2,237 |

Each of these ten cases used one native membership probe. The 2,600-byte
ceiling leaves about 16% headroom above the highest observed value, 2,237.
These values qualify the fixed budget; they do not show a net reduction in
total guard allocation relative to the preceding targeted qualification.
These are dated local measurements of the targeted cases; they do not establish
a full-suite release qualification or a minimum allocation for every workload.
The [scale regression](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Concurrency/NativeTrackedIdentityGuardTests.Scale.cs)
owns the assertion and warm-baseline setup. Existing unrelated-tracker and
forest-insertion allocation ceilings remain separate contracts.

Bulk planning and full rebuild use iterative compact buffers and bounded
64-entity write batches. The qualification target is at most 512 MiB additional
managed heap for one million operated nodes above the existing caller inputs.
Provider and framework allocations during the operation remain included;
pre-existing application payload and an already populated change tracker are
outside that additional-heap budget.
Full tree validation and rebuild plans retain at most 1,024 individual issue
keys while counting every violation by code. Public reports use this bounded
issue-key representation.

When a coordinated `SaveNestedSetChangesAsync` has hierarchy candidates, it
snapshots every tracked EF entry before its payload save. An unrelated save
without hierarchy candidates takes the ordinary EF path. Snapshot cost grows
with tracked entries and mapped values, including unrelated entity types. The
payload save and `SavingChanges` callbacks can alter those entries. A later
hierarchy failure must restore their original tracker state. Filtering the
snapshot to affected trees would violate that rollback contract. Keep
long-lived contexts and unrelated tracked graphs out of latency-sensitive
hierarchy saves. See
[EF Core SaveChanges](https://learn.microsoft.com/en-us/ef/core/saving/),
[save events](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/events),
and [tracked entry state](https://learn.microsoft.com/en-us/ef/core/change-tracking/entity-entries).
The `TrackerSnapshotAllocationTests` isolate this cost: 20,000 tracked entities
with 15 scalar properties must remain below 60 million bytes of warm
allocation, and mutable values must restore after a failed save.

Library-owned insertions validate their plan once at EF's relational persistence
step. That check reads only EF's pending write set and snapshots only ordinary
callback writes; queries and saves outside a managed insertion add no
interception work beyond one weak-table lookup per save.

`SingleInsertScaleTests` separately measures an explicit single-node insertion
with 20,000 clean, unrelated tracked rows. The current-source local .NET 10.0.12
SQLite run on 2026-10-04 observed 427 additional allocated bytes per tracked row:
752,192 baseline bytes and 9,300,416 bytes with the unrelated tracked set.
The unchanged 500-byte regression ceiling detects substantial tracker-wide
allocation growth. The earlier 2026-09-24 observation was 419 bytes per row;
these dated observations do not establish a throughput comparison or an
allocation reduction for the complete insertion.

## Qualification targets

### Insertion snapshot allocation observation

On 2026-10-04, the same warmed .NET 10.0.12 harness compared 20,000 unchanged
identity refreshes before and after typed snapshot reuse. Thread and precise
process allocation counters agreed:

| Identity shape | Before, bytes/refresh | After, bytes/refresh |
| --- | --- | --- |
| Assigned integer | 112 | 0 |
| Assigned Guid | 128 | 0 |
| Compound integer | 176 | 0 |
| Self-FK integer | 472 | 0 |
| Generated integer sidecar | 112 | 0 |
| Mutable binary | 1,064 | 1,064 |
| Converted mutable reference | 112 | 112 |

These are isolated refresh observations, excluding the initial lifecycle handle,
first snapshot, model compilation, payload writes, and generated value changes.
They do not assert an allocation-free insertion or import. Mutable identities
retain independent snapshots and native reinstallation. Differing member/model
types retain EF's sentinel-aware getter; the integer property-bag control
observes 48 bytes per refresh at that framework boundary. The
[refresh regression controls](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/BulkImport/InsertionRefreshAllocationTests.cs)
protect allocation and semantic behavior together. Million-node measurements
below independently qualify occupied heap and complete-operation traffic.

On 2026-10-08, warmed controls separately measured 10,000 unchanged `Matches`
and `PrepareDetach` calls on .NET 10.0.12. Integer, Guid, compound, generated,
shadow, string, and backing-field keys allocated zero bytes in both paths.
The integer property-bag control retained the native object getter: 48 bytes
per `Matches` call and 72 bytes per `PrepareDetach` call. Regression ceilings
allow 2,048 fixed bytes per measurement in addition to these fallback costs.
This includes the additional installed-relationship-key comparison; current
values alone cannot reveal an intermediate key registered by change detection.

Initial lifecycle capture is a separate control: 10,000 already-tracked,
independent integer roots allocated 2,720,000 bytes, or 272 bytes per root,
excluding model initialization, EF tracking, and the result array. Its ceiling
is 280 bytes per root plus 2,048 fixed bytes. A mutation control that eagerly
created the unused dependent-bucket list allocated 304 bytes per root and
failed this test. Bucket storage is now allocated only for a real membership.
These measurements do not update the million-node occupied-heap observations
or establish hosted-runner capacity headroom.

Sources: [comparison and capture budgets](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/BulkImport/InsertionRefreshAllocationTests.Comparisons.cs)
and [identity recovery controls](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/BulkImport/InsertionRefreshAllocationTests.Recovery.cs).

### Capacity targets

| Area | Target |
| --- | --- |
| Correct tree size | 10 million nodes readable and fully validatable |
| Width | 1 million direct children without recursive client traversal |
| Depth | 100,000 levels without call-stack recursion |
| Bulk import | 1 million nodes in one atomic batched operation |
| Bulk/rebuild heap | At most 512 MiB additional managed heap for 1 million nodes |
| Anchor query | One SQL command without loading the anchor entity first |
| Single-table structural mutation | Command count independent of node count, width, and depth |
| Independent writers | At least 64 writers on different trees without a global library lock |
| Hot tree | 64 writers serialize without deadlock or structural damage |
| Cross-tree move | Stable database lock order for opposing moves |

These are release qualification targets, not latency guarantees for arbitrary
hardware. Repository tests enforce deterministic width, depth, planning,
command-count, affected-row, and concurrency properties.
Mapped payload deletion across TPT or split tables instead uses bounded key
batches; its command count grows with the number of deleted rows. The
single-table structural target does not promise constant command count for
every mapping or for bulk import and rebuild.
The [regression matrix](regression-coverage.md) names the positive, negative,
and adversarial mechanisms behind these checks. Neither its entries nor these
targets imply universal regression coverage or certification of a particular
hardware throughput.

### Real relational capacity cases

The shared [capacity suite](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Capacity/CapacityTests.cs)
executes through the existing provider projects. Its fixture contains actual
persisted rows, rather than an in-memory plan extrapolated to a larger size:

- Ten million nodes are streamed through the public tree query and checked by
  Full validation. An independent oracle checks every structural field through
  scalar aggregates over primary-key ranges of at most 100,000 rows. It also
  counts missing expected keys and unexpected keys outside the fixture range.
- One root and one million direct children pass through public atomic forest
  import. The original caller inputs have non-sentinel structural values,
  which also exercises retained rollback snapshots.
- A chain reaches Depth 100,000, with 100,001 stored nodes. Full inspection
  and public rebuild exercise iterative traversal on the real database.
- One million corrupted rows pass through public rebuild in 15,625 bounded
  repair statements. Failure and cancellation at the last batch must undo
  earlier repairs and preserve the registry revision.
- Failure and cancellation at the final import refresh occur after every
  million-node payload wave. All inputs and the tree reservation must restore.

Read-only fixture seeding uses bounded server-side `INSERT SELECT` batches
after registering the root through the public API. It does not qualify bulk
import throughput. Import and rebuild cases use their production paths and
aggregate-only observers; the observer never retains a list of SQL commands
or parameters. Small 129-node cases verify wide/deep seeding and the same late
failure boundaries before a full-size run.

The oracle does not materialize entities or increase command timeouts. Its
root/child branches explicitly check nullable Parent values, avoiding nullable
inequality compensation inside a table-wide predicate. The range limit bounds
each statement rather than reducing the total fixture checked. Oracle
[regressions](regression-coverage.md#capacity-coordinate-oracle) cover wide and
deep fixtures, every structural field, missing/replacement keys, and damage on
both sides of the 100,000-row boundary.

Ordering-refresh arrangements also use native `INSERT SELECT` batches, with
at most 10,000 children inserted per statement and only the root tracked by EF.
Root registration and every child batch share one transaction. Mapped parameters
preserve provider representations, including binary Guid and converted enum
values. This bounds client setup memory and insert cardinality without reducing
the measured forest or changing command timeouts. It is a fixture control,
not a production throughput measurement. The [seeding regressions](regression-coverage.md#ordering-refresh-fixture-setup)
verify stored values, command counts and early/late failure rollback.

Heap measurements warm the provider and model first, then collect a baseline
after the caller-owned entities, topology, and payload exist. The inputs stay
alive throughout the operation. Provider and framework allocations above that
baseline remain included. A five-millisecond timer and command-boundary
samples record a conservative occupied-heap estimate from completed-GC heap
minus fragmentation, plus precise managed allocations since a preceding
collection observation. Stable index reads and full-GC start anchors cover
collection races and background snapshot publication. Each full-start anchor
keeps its preceding sample's generation-zero collection count. The newer anchor
is used only when that count is strictly below the snapshot's collection-start
index and the index does not exceed the accepted current count. Uncertain or
wrapped counter epochs, including completion followed by another full start,
keep the earlier anchor. This avoids charging an entire preceding cycle when
its allocation history is provably unnecessary. Snapshot adoption remains
monotone by start index and can conservatively retain an older completion
record. This is not an exact
peak or an upper bound on an unobserved transient spike. Total allocation traffic is
reported separately. Samples and assertions finish before verification reads.
The precise allocation counter has a documented performance cost. These heap
samples use it to account for allocations after a completed GC, rather than
only to read a lifetime total. Replacing it with an approximate counter could
understate the observed occupied heap. Local elapsed times include this
instrumentation; they do not establish a GitHub runner time budget or a
production latency guarantee. The separate total-allocation measurement uses
precise counters at the operation boundaries.
The 512 MiB assertion applies to the documented integer-key, Guid-TreeId,
integer-Scope fixture and its fresh operation context. It cannot bound
arbitrarily large binary keys, application callbacks, or a million independent
root plans. Full validation of ten million nodes retains linear structural
state and has a different memory requirement.

The isolated plan test uses blocking collections and completed heap minus
fragmentation for retained bytes. On the qualified macOS arm64 .NET 10.0.12
runtime, `GC.GetTotalMemory` reported approximately twice the independently
allocated size of promoted small objects; it is therefore not used for these
capacity assertions. For 20,000 inputs, the current plan retained approximately
3.14 million bytes with sentinel structure and 6.18 million bytes with fully
prefilled child structure, below the unchanged 6.7-million-byte plan budget.
These plan measurements do not replace actual public million-node operations.

The public forest path skips redundant cross-plan validation indexes when it
contains just one tree, whose plan has already checked references and assigned
keys. On 2026-10-09, the isolated 20,000-node preparation regression measured
2,523,904 additional allocated bytes above standalone planning before the fix,
and 3,448 bytes afterward. It stops before connection opening and measures
allocation rather than occupied heap. Cross-tree rejection remains checked
before the write boundary. These measurements do not establish the revised
SQL Server hosted capacity result; the 512 MiB assertion remains unchanged.

The [heap observer](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Infrastructure/Observation/ManagedHeapObservation.cs)
has [held-allocation controls](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Observation/ManagedHeapObservationTests.cs).
The independent calibration runs the actual helper in the existing Unit
executable before xUnit initializes. It holds 100,000 objects or small arrays,
counts their allocation bytes independently and retains the original +/-10%
accuracy bound. Runner-state reclamation cannot be hidden by a wider tolerance;
ordinary test invocations still use xUnit's public console runner.
The same controls include a captured native scalar tuple, qualified-anchor
cases, unknown identities, and the completion/start race that would otherwise
understate occupied bytes. They verify measurement safety rather than relaxing
the operation's dataset or memory limit.
Microsoft documents [last-GC heap size](https://learn.microsoft.com/en-us/dotnet/api/system.gcmemoryinfo.heapsizebytes?view=net-10.0),
[fragmentation](https://learn.microsoft.com/en-us/dotnet/api/system.gcmemoryinfo.fragmentedbytes?view=net-10.0),
and [precise lifetime allocation](https://learn.microsoft.com/en-us/dotnet/api/system.gc.gettotalallocatedbytes?view=net-10.0).
The [qualified runtime source](https://github.com/dotnet/runtime/blob/v10.0.12/src/coreclr/gc/gc.cpp)
defines the segmented estimate and background snapshot ordering.

### Local capacity observations

The following public operations completed on 2026-10-04 using the same frozen
source and .NET 10.0.12 binaries. Each import contains one root and one million
direct children, with 15,626 payload saves. Each million-node rebuild completes
15,625 repair batches. The additional occupied figures are the maximum sampled
conservative estimates described above; total allocations include transient
provider and framework traffic across the whole operation.

| Engine | Import additional occupied MiB | Import total allocated GiB | Rebuild additional occupied MiB | Rebuild total allocated GiB |
| --- | ---: | ---: | ---: | ---: |
| MySQL | 483.6 | 15.58 | 233.2 | 4.02 |
| MariaDB | 482.8 | 15.57 | 228.0 | 4.02 |
| PostgreSQL | 484.9 | 16.88 | 217.0 | 4.85 |
| SQL Server | 487.7 | 16.44 | 229.7 | 4.37 |
| SQLite | 483.2 | 19.06 | 161.4 | 3.89 |

MiB and GiB use binary units. The unchanged limit is 512 MiB of additional
occupied heap for these operations, not 512 MiB of lifetime allocation traffic.
Deep public rebuilds on the 100,001-node chain observed 53.7-71.7 additional
occupied MiB. These are observations of the documented fixtures, including
their sampling overhead; they are neither exact peaks nor allocation or
throughput comparisons with the preceding implementation. The SQL Server host
limitation below applies to its row as well.

The five complete capacity runs passed 70/70 cases, with 14 per engine and no
skips. This includes exact restoration after million-node late failure and
cancellation for both import and rebuild. These local results do not establish
hosted CI/RC qualification or remove the SQL Server supported-host requirement.

Run the capacity suite separately when selecting focused feedback; the cases
remain discoverable ordinary tests with `Category=Capacity`:

In Rider, the local **Skip tests from categories = Capacity** preference keeps
ordinary mixed Run All selections free of these datasets. Select and run only
the **Capacity** category node to execute capacity cases across the provider
projects; Rider's category-only selection overrides this exclusion. Ignored
cases in an ordinary Rider session are not executed evidence. These personal
IDE preferences do not filter CLI, CI, or RC runs. See
[Rider setup](../CONTRIBUTING.md#rider-feedback-and-capacity-qualification).

```bash
dotnet test tests/Doka.EntityFrameworkCore.NestedSet.Sqlite.Tests/Doka.EntityFrameworkCore.NestedSet.Sqlite.Tests.csproj \
  -c Release --filter 'FullyQualifiedName~.Sqlite.CapacityTests.' \
  --logger 'console;verbosity=normal' --logger 'trx;LogFileName=sqlite-capacity.trx'
```

Use the PostgreSql, SqlServer, and MySql projects for their owning engines.
The MySql project has separate `.MySql.CapacityTests.` and
`.MariaDb.CapacityTests.` filters. Execute full-size provider qualifications
serially on a shared local Docker host; each fixture releases its own database.
When selecting all capacity cases in Rider on that host, temporarily set
**Maximum number of test runners to run in parallel** to `1` for this deliberate
qualification, then restore your ordinary regression setting. The allocation
collection serializes cases within an assembly; it does not serialize separate
provider processes or reserve Docker memory for their servers.

SQL Server capacity contexts remove the connection timeout for this functional
suite only. SqlClient 6.1.6 reuses that timeout for native transaction rollback,
so its default 15-second wait would also impose an implicit undo-speed gate.
The local million-row rollback diagnosis observed continuing server undo for
about 33 seconds after the deliberately injected failure. Ordinary command
timeouts, dataset sizes, memory limits, and exact rollback assertions remain
unchanged. The test runner owns end-to-end termination; this fixture setting
is not a production configuration recommendation. SQL Server under x64
translation on the local Arm host remains outside Microsoft's supported-host
release qualification. See [provider recovery timeouts](transactions-and-locking.md#savepoints-and-failure-handling)
and [supported-host requirements](support-and-qualification.md#provider-and-engine-matrix).

The [server 64-writer suite](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Concurrency/ConcurrentCapacityTests.cs)
holds 64 distinct existing registry locks simultaneously on server engines,
then commits and fully validates all trees. The [common writer suite](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Concurrency/ConcurrentWriterTests.cs) releases 64
writers against one existing root and verifies dense persisted positions on
all engines. A canceled server contender retains both callers' earlier
application writes. SQLite cannot hold independent write transactions
simultaneously because its database-wide writer boundary precedes tree locks.

## Deterministic CI and RC measurements

Shared integration measurements live in the non-runnable specification library
and execute through concrete suites in the owning provider projects. Exclusive
measurements, such as `SingleInsertScaleTests`, remain in their provider project.
Each provider assembly registers its own `Allocation measurements` collection
with parallelization disabled. Containers remain shared per engine within that
assembly, while databases keep their existing class or collection ownership.
See [test project ownership](implementation-design.md#test-project-ownership).
The source layout does not change measurement methods, budgets, or dated results.

GitHub CI does not gate on elapsed time, throughput, process working set, or CPU
model. Hosted runners can change hardware and load. CI instead verifies:

- final structure and payload state;
- SQL command and hierarchy-update counts;
- affected-row formulas and absence of N+1 work;
- query shapes and index paths where provider plans are stable;
- iterative wide and deep planning; and
- bounded allocation form.

## Development benchmarks

The [BenchmarkDotNet guide](../benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks/README.md)
owns launcher options, fixture lifetimes, diagnostics and output formats.
Measurements run explicitly in isolated .NET 10 Release processes; they impose
no CI/RC throughput threshold or automatic merge decision.

```sh
dotnet run --project benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks \
  -c Release -- --list flat

dotnet run --project benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks \
  -c Release -- --engine MariaDb --nodes 1000,10000 --shape Wide,Balanced \
  --trees 4 --tracked 0,1000 --filter '*MoveBenchmarks*' \
  --artifacts artifacts/benchmarks/move-mariadb

dotnet run --project benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks \
  -c Release -- --diagnostics --engine SqliteMemory --nodes 10000 \
  --shape Balanced --trees 4 --filter '*InsertBenchmarks*' \
  --artifacts artifacts/benchmarks/insert-sqlite-diagnostics
```

Every execution needs an explicit benchmark selector. Listing and help acquire
no database. Core-only cases likewise have no database owner. Native profiles
use the existing digest-pinned Testcontainers with fixed resources, no
application credentials and pooling disabled. Run comparisons sequentially;
other tests or benchmarks can contend for the same host.

Reset-dependent families restore the database/tracker outside timing and await
one operation per observation. Their default job has three warmup and twelve
target iterations. Read-only reports and expression construction use throughput
calibration. A dry run checks execution/export, not comparative performance.
Consumer validation, query construction, materialization and result creation
inside the selected operation remain measured work.

Retain raw results and credential-free provenance: start/end source identity,
SDK/runtime, package versions, engine/image, resource limits, dataset and actual
job settings. Changed source, hardware, packages or data starts a new comparison
series. Preserve adverse observations and outliers. Warmup does not establish
stationarity; inspect each launch's chronology and operation count.
Allocated bytes and GC counts do not measure retained heap, peak memory or
server memory. Process-wide asynchronous observations can include background
provider allocations. Untimed command diagnostics observe SQL/effect boundaries
separately; their UPDATE counts exclude result-producing reader writes and do
not represent physical I/O or complete write amplification.

The [performance qualification](qualification/query-performance.md) separates
construction, full reads, materialization and the isolated EF ownership
experiment. The [benchmark regression project](../tests/Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests/Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests.csproj)
runs in Debug and Release; Debug rejects actual measurements before resources.
Functional lifecycle tests validate workload structure independently of the
test build's JIT optimization status. A separate control verifies that the real
configuration retains BenchmarkDotNet's critical optimization validator: Debug
assemblies fail measurement eligibility and Release assemblies pass it. No
production validator is disabled to make functional tests pass in Rider.

## Query composition and actual consumer profiles

The [composition contract](query-composition.md) adds expression-building
operators, not another database engine. Construction must not read a node,
enumerate a payload, compile an application predicate, or retain a context in a
global cache. Execution cost still depends on the relational result and plan.
With options registration, source-sensitive ancestor composition and downstream
filters over its owned markers can defer expression work to EF compilation.
Cold compilation and cache-hit execution therefore need separate complete-reader
observations. A reduction in public builder time/bytes does not by itself prove
that end-to-end work or allocation decreased. Compilation-local root sharing
owns no result, context or application query cache.

| Query shape | Work that must remain visible in its qualification |
| --- | --- |
| Multiple subtree anchors | Every requested anchor's range and every matching occurrence |
| Ancestor projection | Actual structural depth, including hidden intermediate levels |
| Any owner | Visible matching-owner existence, without asserting a complete path |
| Nearest owner | Candidate selection before value rejection; no farther fallback |
| Complete path | Visible stored direct edges over the selected upper-to-lower interval |
| Mixed-direction walk | Every intermediate occurrence and each leg's independent conditions |
| SignalLab report | Actual metadata predicates, three fact sources, catalog and grouped scalar results |
| Supervisor selection | Actual role, membership and routing predicates, per-origin reduction |
| Supervisor chain | Finite snapshot-bound frontier batches, cycle and overflow statuses |

Combining overlapping anchors intentionally repeats occurrences. If there are
`a` anchors with `r` matching rows each, an unreduced projection can contain
`a * r` rows. A complete path of depth `d` can require `d` edge checks for each
relevant occurrence. Nested sets do not make unrestricted overlap or every
business predicate constant-cost. Select anchors and payloads explicitly, keep
domain predicates in SQL, and aggregate before materializing reports.

The shared path proof uses indexed identity/direct-parent checks for distances
zero, one and two, then retains the general visible-edge proof for longer
paths. Nearest handles a visible self candidate before its strict-ancestor
lookup. These are equivalent execution shapes with adversarial regression
coverage; they are not depth limits or permission to skip a hidden edge.
For one edge, its already visible endpoints supply the typed Parent comparison
directly. For two edges, one visible middle alias supplies both links, both
containments and both edge predicates. Nullable converted keys and the
principal collation still define equality; a forest still checks both TreeIds.
Scalar CASE expressions keep the path proof out of observed premature
semijoin evaluation. The database still chooses its actual execution plan.

Endpoint validation removes a repeated visibility lookup only when an alias
comes from the exact bound query expression. A different facade, raw set,
optional outer-join endpoint, mixed set branch or changed query-filter policy
retains its membership check. This is a conservative source proof, not a
substitute for Scope/TreeId isolation or an assumption that equal CLR types
have the same mapping.

The source-wide visibility proof is reused only within one compositor call;
each selected alias still needs its own provenance proof. Internal path aliases
already bound to that source retain TreeId equality in a forest, while a
tree-bound query needs only their coordinate relation. External aliases retain
the complete mapped identity and source-membership check. Direct parameter
replacement avoids allocating synthetic lambdas and argument arrays per edge.

The [query-cache controls](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/CompositionQueryCacheTests.cs)
observe actual asynchronous EF compilation. Changing captures, Scope, TreeId
and fixed-cardinality anchors must reuse a warm shape while a changed projection
requires another compilation. Query-cache reuse does not prove small commands,
cheap parameter processing or efficient native plans. The
[semantic/native-work summary](qualification/query-composition.md) keeps those
separate from historical construction/reader observations.

The sample applies `EF.Parameter` to finite catalog arrays and routing indices.
This per-expression override keeps their collection transport independent of
criterion count without changing the application's global EF options. The
cardinality-change control switches from one to sixteen criteria, then changes
their tenant and values: it checks both result freshness and actual compilation,
command and binding stability. EF still extracts parameters and computes the
cache key on each ordinary execution; reusing a query or expression object does
not remove that work. See the [EF 10 collection change](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes#parameterized-collections-now-use-multiple-parameters-by-default)
and [EF's execution pipeline](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore/Query/Internal/QueryCompiler.cs).

Query-only scalar reads use the exact mapped CLR property or field when EF's
metadata resolves that member to the same property. This reduces method-marker
arguments and reflection during warm parameter extraction. Shadow, indexer and
complex roles retain their EF property markers; neither branch invokes an
application getter. Renamed columns and differently named backing fields are
qualified by actual scalar queries, including deliberately throwing getters.
No query/context cache, compiled application predicate or EF internal service
replacement is introduced.

Containment retains both coordinate comparisons and states their implied
overlap limits explicitly. Under the stored `Left < Right` invariant, this
preserves the same results while exposing finite ranges to either existing
coordinate index. Equality on Scope/TreeId followed by a bounded coordinate
range fits the documented
[PostgreSQL multicolumn B-tree rules](https://www.postgresql.org/docs/17/indexes-multicolumn.html).
Actual plans still qualify whether the optimizer uses that access path.

Doka's legacy strict ancestor route uses an independent relation of distinct
NodeKey, TreeId, Left and Right coordinates for visible nonleaf owners. Mapped
owners rejoin by identity after containment. The relation carries no application
payload and cannot deduplicate caller occurrences, but adds a population pass
and temporary structural storage. It remains available when a selection needs
hierarchy decorrelation or either required public provider capability is absent.

Eligible independent selections with the verified source binder instead share
selected structural coordinates across their source envelope and exact pairs.
Source-derived Scope/Tree points expose the existing Right index without a
generated ancestor property. The ordinary strict route also requires the existing
verified ordered-join capability. Only positively identified MySQL receives a
second binding over matched identity pairs without deduplicating occurrences.
MariaDB and unknown engine identification keep source-only binding. Both routes
use ordinary source hydration and directed owner hydration. MySQL's recursive
matched-pair completion fence separates repeated direction changes from one
merged payload/EXISTS optimizer search. Each binding
completes its own source; distinct logical operations are not one global source
evaluation. This boundary can limit downstream predicate pushdown and adds
intermediate work. Necessary envelope limits never replace exact containment,
visibility, or final mapped endpoints. See the
[ordinary strict-source qualification](#ordinary-strict-source-bounds-and-membership-witnesses)
for its observed work and additional construction cost.

Unranked UserGroups requests retain the inclusive relation only when an explicit
start group and the membership primary key prove at most one origin. Other
requests use strict ancestors plus exact authorized self memberships. Necessary
membership keys select the strict sources, then the original authorized origins
rejoin before group windows, roles, routing, and path decisions. No count query,
guessed cardinality, or weakened hierarchy-source guard selects the path.

An additional MySQL LATERAL experiment did not meet the selective-work target:
one selected row still caused 100,001 owner index tuples to be visited. It was
not adopted. A public configured-engine API is sufficient to select a supported
dialect, but is not proof of an efficient ancestor access path. A separate
source-sharing/index contract requires native proof before replacing the
corresponding strategy. The bounded ordinary route and optional generated-index
route have separate qualification; neither is inferred from the engine name.

### Ordinary strict source bounds and membership witnesses

The frozen 2026-10-25 native work cohorts below precede the later matched-pair
completion boundary. They qualify their recorded source-bounded strategy,
not automatically that subsequent query shape. They use a wide tree with
10,000,000 stored nodes and no generated ancestor index. Strict upward/downward
composition retains one query command, four parameters, the expected scalar occurrence bag, zero
materialized entities, and an empty tracker. Each of its two owner-coordinate
range iterators reports the following actual rows per invocation:

| Selected sources | MySQL owner rows | MariaDB owner rows | Scalar result rows |
| ---: | ---: | ---: | ---: |
| 1 | 1 | 1 | 1 |
| 10 | 10 | 10 | 10 |
| 100 | 100 | 100 | 100 |
| 1,000 | 1,000 | 1,000 | 1,000 |

These counts describe iterator output before the remaining nonleaf filtering,
not distinct physical pages, bytes, or total query work. MariaDB's dynamic
range-check wrappers report zero full scans and use the existing Right index;
the nested estimated `access_type=ALL` label alone is not a measured full scan.
MySQL's replanned range iterators likewise have actual bounded counts despite
broad estimated row counts. The earlier MariaDB cohort read 10,000,000 rows in
each owner materialization. The failed earlier MySQL ten-million run has only
estimated EXPLAIN evidence, so it supplies no actual before/after latency ratio.
The successful older MySQL million-node balanced case is a different workload.

Supervisor profiles use necessary membership witnesses and rejoin exact
authorized origins before all existing group and candidate decisions. Both
engines completed the ten-million-node capacity case, including full structural
validation and the mixed/query-consumer assertions. The one-reader supervisor
observations with 1/16/100/1,000 origins and 1/4/16/16 matching routing fields
retain source/status rows, all expected evidence, zero entities, and 25
parameters. MySQL's 1,000-origin/16-field observation still took 1,035.717 ms,
versus MariaDB's 22.273 ms in that run: its plan repeats requested metadata
lookups rather than scanning all ten million owners. This is residual work,
not evidence that routing optimization is exhausted or that one engine is
universally faster.

The ordinary envelope cohort constructs approximately 142 KB per operation,
versus approximately 114 KB in the earlier fallback; the supervisor witness
cohort constructs approximately 105 KB versus 98 KB and uses 25 rather than 23
parameters. Source sharing, envelopes, and the authorization rejoin add
expression structure. Native work improvements do not establish an allocation
win, cheaper complete reads, or regression-free deep/broad behavior. These are
individual capacity observations, separate from paired BenchmarkDotNet
construction/read/materialization comparisons and their uncertainty. No runtime
or allocation budget was relaxed and no hardware timing gate was introduced.

The subsequent all-engine matched-pair boundary retained eight targeted native
walk variants and 86 targeted Unit controls, but failed the actual MariaDB
UserGroups branched-chain sample. Two diagnostic controls recorded cgroup
`OOMKilled=true`, exit 137, with the existing 30-second command timeout and the
same two-CPU/two-GiB container limits. This failed cohort is retained; its narrow
walk success did not establish complete-consumer viability.

The corrected route adds matched-pair completion only for positively identified
MySQL, using the existing public engine API. MariaDB and unknown identification
keep source-only binding, with ordinary source and directed owner hydration.
The targeted corrected cohort passed 32 native cases, including all eight
original `UpDownUpRetainsTheCompleteWalkBag` variants and 24 envelope/work/negative
cases. The actual three-sample SQLite/MariaDB cohort passed 33 child-process
cases under the same recorded two-CPU/two-GiB MariaDB limits. These are targeted
semantic and sample checks, not final full-suite, ten-million-node plan, or paired
construction/read/materialization evidence for the changed query. MySQL's
intermediate-key materialization and possible lost predicate pushdown remain
part of that separate cost qualification.

Sources: [ordinary envelope construction and rejection controls](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/IndependentAncestorEnvelopeTests.cs),
[sparse identity and visibility semantics](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.SparseAncestorIdentity.cs),
[mixed-direction capacity profiles](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Performance/CompositionCapacityProfiles.Mixed.cs),
[supervisor witness controls](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Samples/UserGroups/GroupSupervisorTests.OriginWitness.cs),
and [ten-million-node qualification](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Capacity/CapacityTests.cs).

### Optional ancestor index and shared-source boundaries

[`HasAncestorIndex()`](configuration.md#optional-ancestor-index) adds a generated
nonleaf flag and an ascending `(Scope?, flag, TreeId, Right, Left, NodeKey)`
index. Existing bounds/ordering indexes remain. It is opt-in because coordinate
writes maintain another index and the index occupies database storage.
The [capability table](configuration.md#current-query-capabilities) defines
read eligibility; [implementation design](implementation-design.md#optional-ancestor-index-and-statement-local-source-binding)
defines source sharing, narrow coordinates and late mapped hydration.
A schema option or provider feature alone proves no workload benefit.

Compare the exact source selection and downstream predicates, complete reads,
construction/read allocations, coordinate writes and allocated index pages on
both native engines. Attribute application-table work separately from hierarchy
work and keep application indexes identical. `Any`, `Contains` and negated
existence need their own plans: local directed edges can change semijoin eligibility.
The indexed inclusive strategy directs structural matching and final owner
hydration separately while leaving source hydration ordinary. Both native
engines must retain the complete bag and unchanged work budget; identical SQL
or correct result counts alone cannot establish a stable efficient plan.
Do not merge source occurrences or discard collection identifiers to make a
plan faster.

The [ancestor-access summary](qualification/query-composition.md#optional-ancestor-access)
retains higher storage/write costs, slower indexed chain/balanced reads, failed
controls and substantial repeated temporary candidate CPU despite reduced
physical base access. Its `64 * (S + P)` fixture allowance is not a universal
B-tree complexity bound or proof of output-proportional CPU. Requested source
occurrences, distinct selected nodes and completed structural cardinality are
separate quantities. Missing counters remain unknown, never zero.

#### Endpoint provenance construction

Endpoint inspection uses operation-local lexical scopes and memoized stable
paths; it never caches a context, caller capture or completed query globally.
Named mappings, independent paths and mutable query carriers cannot borrow a
visibility proof. Opaque object-typed query carriers do not gain provenance by
casts, `as` or `AsQueryable`, and no application getter runs to recover it.

The [endpoint allocation controls](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/CompositionSourceBindingEndpointTests.Scaling.cs)
retain fixed 64/128/256 KiB limits at shared-source depths 12/24/48 and a 256 KiB
ceiling for alternating depth-24 paths. The historical lexical-memo repair
reduced that accepted/rejected depth-24 fixture from approximately 19.6 GB to
99,888/97,056 bytes. Later steady-state controls warm CLR metadata once but
create a fresh operation-local proof for measurement: accepted 90,176 bytes;
foreign-mapping rejection 87,440-87,480 bytes. Those are different measurement
boundaries, not another before/after ratio. All require zero construction I/O
and retain adversarial mapping/capture rejection.

### Deterministic construction and transport budgets

Measure a warmed, finalized EF model and the complete actual builder, including
the endpoint checks and expression substitution. Context/model creation and
database setup are separate costs. Scalar criteria must remain parameters;
adding one criterion must not copy a stored hierarchy or build a new compiler.

| Boundary | Fixed contract |
| --- | --- |
| SignalLab fixed four-criterion builder | At most 128 KiB for Any; 160 KiB for Nearest; 232 KiB for Doka MySQL/MariaDB Nearest |
| Added signal scalar criterion of the same pattern | At most 4 KiB additional allocation |
| SignalLab request snapshot fixture | At most 24 KiB for 20 types, 80 unique criteria and 160 requested criteria |
| SignalLab empty-total grid fixture | At most 76 bytes per cell plus 2,048 bytes for 6,144 selected cells |
| Supervisor four-match builder | At most 256 KiB allocated per complete build |
| Added supervisor scalar match | At most 4 KiB additional allocation |
| Scalar hierarchy/report projection | No hierarchy entity materialization or tracker growth |
| Scalar or aggregate composed query terminal | One async reader command, with its caller token |
| SignalLab validated report | One catalog read and one grouped report read in the same snapshot |
| Supervisor selection | Server-side ranking/uniqueness and bounded status/evidence transport |
| Chain frontier | Bounded batch, transport, path-copy and cycle budgets before allocation |

The sample builder tests use the actual sample assemblies. A generic query
with fewer predicates is not a substitute for these consumer budgets.
Snapshot and grid allowances describe their fixed unit fixtures, including
owned buffers and the final result cells. Nonempty SQL totals also require
their duplicate-detecting lookup; the empty-total grid allowance does not
claim to include that population. These checks are independent of the complete
read's process-wide allocation measurements.
Additional collection includes and EF's `AsSplitQuery()` policy can produce
multiple statements; the single-reader budget above applies to the qualified
scalar and aggregate shapes, not arbitrary entity loading.
Both use the shared C# allocation protocol after ten warmup builds per shape:
100 complete builds for the absolute mean, or 1,000 adjacent alternating
baseline/expanded pairs for the incremental mean. Every sample contributes;
there is no trimming, favorable-sample retry, or changed budget. Independent
protocol tests verify exact call/order counts and retain a continuous 8 KiB
regression above the 4 KiB limit.

The chain reader applies its overflow probe in SQL after semantic selection
and before async enumeration. EF can internally buffer when a retry strategy
is active; a client-side row counter alone would act too late. Returned-row
limits bound transport and buffering, not server-side scans or sorting. See
[EF's internal-buffering documentation](https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying#internal-buffering-by-ef).

The pure person reducer keeps one canonical occurrence per distinct person,
sorts those people, and retrieves the selected person's original evidence.
For `R` occurrences and `P` people this takes `O(R + P log P)` work and `O(P)`
rank storage, plus the returned evidence. Highest priority and Nth-person rank
remain different rules; ranking must not replace the continuation rule.
Server reduction similarly separates canonical ranks from the evidence join.
These source-level bounds do not establish a database optimizer's complexity.

Unique and highest-priority selection use linear winner/cursor passes without
building a distinct-person rank map. Nth-person selection retains its rank
storage. Unique, priority and Nth-person selection still snapshot caller-owned
winning evidence slots; `All` retains its existing direct evidence forwarding. Only
the internal reader's explicitly owned fresh buffer may be retained directly
when every occurrence belongs to the winner. Chain continuation retains the
hop and shared prefix node without allocating a one-element occurrence array
for each logical path. Convergent paths do not preallocate routing storage from
the unrelated full frontier count.

Type-group ordinals select full mapped paths before membership, routing or
edge validation. Eligible-group ordinals first qualify and deduplicate groups,
then select an ordinal and rehydrate its exact visible endpoints by key.
Every returned person still passes the same membership, role and routing
conditions. The endpoint/date-only path proof is reused for that selected
group rather than embedding a second complete hierarchy expansion. Native
PostgreSQL controls count emitted and join-rejected tuples separately, each
multiplied by actual loops; they reject a small final result backed by a
quadratic intermediate.

Both single-group windows admit at most one found group per origin. When the
person witness proves a unique person, that group's membership key also proves
one occurrence, so its group, distance and priority are already complete
evidence. Those windows do not rejoin the complete candidate relation again.
Other windows still restore every original occurrence, including repeated
support in different groups and worse-priority occurrences of the winner.

The sample's period condition reads at most one constant-one witness. Every
matching period contributes the same value and an empty relation contributes
zero, so overlapping periods preserve existence without multiplying edges or
selecting a business period. This keeps the period proof scalar: flattening
its EXISTS had joined repeated parent periods to all children before matching
the exact child key. Null-day and inclusive date rules remain unchanged.

`All` transports one source marker, one marker per visible origin, and each
original candidate occurrence directly in the same reader. It does not join
the complete evidence back to every origin. A nonempty origin therefore costs
one additional marker compared with a null-extended combined row. These are
real SQL rows: chain reads count them against the unchanged `MaxResultRows`
and batch/probe limits before buffering. Empty origins retain their explicit
NotFound status; markers never become evidence or deduplicate people.

Native MySQL/MariaDB rank and origin profiles therefore measure the original
command's seven session `Handler_read_*` request deltas on one open connection,
before executing EXPLAIN. An empty-status control and a fully drained primary-key
lookup calibrate the observation. Work budgets grow with actual people,
occurrences, origins and criteria rather than their pairwise cross-products.
The counters include logical requests, including some optimizer-time work;
they are not physical I/O, rows examined, or peak temporary memory. Index
condition pushdown can examine several index entries within one request. Raw
executed plans and materialized output remain separate observations. See
[MySQL status scope](https://dev.mysql.com/doc/refman/8.4/en/show-status.html),
[handler counters](https://dev.mysql.com/doc/refman/8.4/en/server-status-variables.html#statvar_Handler_read_key)
and [index condition pushdown](https://dev.mysql.com/doc/refman/8.4/en/index-condition-pushdown-optimization.html).

### Shared medium, wide and deep populations

The [composition capacity suite](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Performance/HierarchyCompositionCapacityTests.cs)
uses 10,000 and 100,000 persisted nodes, wide and balanced shapes, anchor counts
1/10/100/1,000, and selective/broad facts. The existing capacity suite supplies
the larger one-million and ten-million populations and the 100,000-level chain.
Composition assertions run on those existing populations; the samples do not
seed a second million-node matrix.

The existing [capacity tests](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Capacity/CapacityTests.cs)
own each population and its lifetime. Each listed test has a combined outcome:
structural work, generic composition profiles and actual consumer profiles
must all complete before that test passes.

| Owning test | Persisted population | Measurement and assertion order |
| --- | --- | --- |
| `TenMillionPersistedNodesAreReadableAndFullyValid` | Ten million wide nodes | Stream and validate; run generic and actual consumer reads; assert all results. |
| `MillionDirectChildrenImportWithinAdditionalHeapBudget` | One root plus one million children | Measure public import; run both read-profile families; retain the original import heap, save-count and parameter assertions. |
| `MillionNodeRebuildWithinAdditionalHeapBudget` | One million nodes, temporarily balanced | Run both read-profile families before repair measurement; restore wide adjacency and corrupt bounds; measure rebuild and assert the original repair contract. |
| `DepthOneHundredThousandValidatesAndRebuilds` | 100,001 nodes at depth 100,000 | Validate and measure rebuild; run generic deep and actual consumer profiles; assert all results. |

Record the owning test, population, read profile and measured phase with a
failure. A composition or consumer failure can prevent later structural
assertions from running; an earlier completed import/rebuild measurement does
not turn that combined test into a pass. Conversely, a read-profile timeout
alone does not establish an import or rebuild regression. Independent xUnit
outcomes would require an explicit shared-population lifetime. Preserve the
existing hooks and populations when diagnosing the current combined cases.

Deep profiles vary 1,000/10,000/100,000 levels and 1/10/100 selected facts.
Dense self-candidate acceptance/rejection, strict dense acceptance/rejection,
a root owner, and controlled nested anchor overlap are separate cases. The
strict forms exclude self and use 100 facts at each depth: acceptance requires
the actual direct parent, and rejection requires the root instead and must
not fall back to it. SQL, parameters and native plans accompany
the observations. Statement count, exact result bags, DTO/entity counts,
construction allocation and tracker state are assertions. Elapsed times are
diagnostic observations, not hardware-dependent release thresholds.

The [consumer capacity adapter](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Consumers/CompositionConsumerCapacityProfiles.cs)
uses the actual signal and UserGroups query builders over read-only structural
views of that same population, with bounded application payload. Its reversible
identity conversion and tree binding have dedicated unit tests. This proves
the consumer query's use of a large structure; it does not qualify migration or
write behavior for `ToView`, nor the storage cost of a large physical period
table. The ordinary sample migration and provider tests cover the actual
physical sample schema separately.

The chain observer retains an executed plan for every reader in each 2/4/8-hop
original-path and expanded-membership profile. Diagnostic replay occurs after
the measured operation. Each reader's file repeats the whole-chain header;
its elapsed time and logical result count must not be summed as per-reader
measurements. SQLite EQP describes access shape, not actual operator row counts.

### Optional paired measurements

`CompositionConstructionBenchmarks` and `CompositionExecutionBenchmarks`
compare generic expressions with an application-LINQ reference of the same
semantics. `SignalReportConstructionBenchmarks`, `SignalReportingBenchmarks`,
`UserGroupSelectionBenchmarks` and `UserGroupChainBenchmarks` call the actual
sample code. Construction and prepared relational execution are distinct
measurements. Every reference must return the same statuses, multiplicity,
dates and branch decisions before its timing is meaningful.

`SignalReportingBenchmarks.Api` measures the actual `ReadAsync`, including request
snapshot and validation, its shared read snapshot, one catalog reader, ordinal
catalog validation, one aggregate reader and scalar grid creation. `Reference`
snapshots the request inside each measured invocation and retains the same
catalog, transaction and result semantics around its explicit-LINQ aggregate.
The current API consumes both readers incrementally; the reference still calls
`ToArrayAsync` before the shared synchronous validation adapters. Their SQL and
result contracts remain comparable, but the end-to-end timings include different
consumption and buffering costs. They do not isolate the query-composition cost.
Preparation and final equivalence checks are outside both measurements. The
[complete signal read qualification](qualification/query-performance.md#historical-report-construction-and-materialization)
records ReadAsync profiling, paired measurements and native-work boundaries;
optional observers are absent from timed consumer contexts.

The chain reference compares bounded application orchestration while sharing
the structural candidate primitive. It is not an independent reimplementation
of the entire SQL translation stack. Report that boundary with the result;
do not attribute every difference to NestedSet's six compositors.

`UserGroupSelectionBenchmarks.Api` measures construction, asynchronous execution
and application result reconstruction together. Its `Prepared` case builds and
asynchronously warms the actual selected query outside measurement, then times
EF execution and scalar materialization. Reconstruction and full reference
comparison run afterwards. `Reference` reads all eligible evidence and applies
the same person-selection rule in the application; it is not an independent
SQL implementation. Prepared execution therefore has a narrower boundary than
either complete consumer and is reported separately.

These BenchmarkDotNet profiles use the existing launcher, provenance and
diagnostics. They add no workflow gate or portable CPU baseline. Run the
functional benchmark tests first, then select a small paired profile on a
stable source tree before expanding the matrix.

### Provider plan boundaries

Provider-specific expressions must preserve the same structural contracts.
Self-first owner decisions, selected-anchor key boundaries and separate self
or non-leaf expansion are physical strategies to qualify against the actual
profile. Query-bearing predicates and direction changes need their own plans:
an early boundary can prevent a later selective predicate from reaching a
broad intermediate relation.

An internal UNION can retain EF row identifiers for owned and ordinary
collection projections, but deduplication can also materialize a large
structural relation before an outer payload filter. That is a strategy
trade-off, not permission to deduplicate a caller's occurrence bag. Qualify
MySQL and MariaDB independently even when they share a provider; a broad-case
improvement can accompany a severe selective-case regression.

Configured-engine detection permits a MySQL-specific related-first correlation
without guessing from model annotations or private options. Its current public
provider prerequisite is listed in
[configuration](configuration.md#current-query-capabilities). Both UNION branches join
independent selected-key sets back to mapped entities so EF retains identifiers
for both endpoints' collections. The boundary contains selected keys, not all
structural pairs. Dated qualification distinguishes the portable-provider
results from that feature-enabled strategy. The current published Doka 10.6.3
declaration does not transfer an earlier private build's performance to the
shipping package; fresh public-binary qualification remains separate.

The subtree-pair work test,
[`ConfiguredProviderPairsPreserveSemanticsAndQualifyAvailableNativeWork`](../tests/Doka.EntityFrameworkCore.NestedSet.MySql.Tests/Querying/Composition/Performance/HierarchyCompositionWorkTests.cs),
executes the same result, command, materialization and native-work observations
for the actual provider installation. Its fixture does not configure
`HasAncestorIndex`, and the test does not require source binding. The public
configured-engine capability determines its assertions: available MySQL support
must satisfy the optimized strategy and unchanged proportional-work budgets;
an unavailable capability must retain the portable strategy and full semantics
with explicit unqualified work observations. The selected strategy cannot choose
the weaker assertion branch, and neither installation creates a skipped test.
This older subtree fast path and the optional indexed-ancestor path have
independent prerequisites. Neither an index option nor the public
`WithMaterializedSource` API alone supplies configured-engine detection.

The SignalLab consumer selects unique owners of filtered facts before projecting
their ancestors and joining the original facts back. Expanding a broad anchor
first materialized ten million structural pairs before its sparse measurement
join. The upward formulation preserves overlapping anchors and every physical
source, metric and day without that unnecessary expansion.

Within that upward formulation, distinct measured Node/Day pairs share their
day-specific path and metadata proof before facts rejoin. A scalar distinct
Anchor/Node/Day boundary prevents merging that proof into each metric/source
contribution. The final join restores every original physical fact and applies
its own measurement-type rule before aggregation. Equal-valued facts are never
deduplicated, overlapping anchors remain independent, and Nearest still selects
its owner before rejecting its value. The additional query-construction work
is reported separately from complete EF allocation and native database work.

The SignalLab builder reuses immutable, request-free fact projections and fixed
owner/member/operator expressions. Its privately owned measurement and period
query roots are bound to each construction; requests, contexts, completed
queries and parameter values are never stored in a global cache. The library's
provenance guard remains intact. Metadata filters run before the temporal
filter because both are pure AND conditions on the same Anchor/Node/Day row.
Nearest still selects its owner from the unchanged tree before testing its
values, so this physical ordering cannot introduce fallback to a farther owner.
Both DISTINCT proof boundaries and restoration of the original fact bag remain.

The catalog reader filters requested `(type, field)` and `(type, field, value)`
bindings together, rather than reading every field and choice of each selected
type or using independent lists that admit crossed pairs. Its alternatives
have logarithmic expression depth without a new criterion-count limit. SQL
collation can still overmatch an alias, so ordinal post-read checks remain
mandatory. Snapshot helpers preserve first-use order and owned buffers while
deduplicating criteria. Incremental catalog validation retains scalar evidence;
aggregate consumption stores selected sums and foreign duplicate identities,
without retaining DTO histories. Both readers complete and dispose before their
post-read rejection checks. Duplicate rejection precedes final checked grid
sizing, and the final reference array retains requested order and
missing-versus-zero cells. Neither path reserves the full sparse output grain
as a lookup capacity.

The sample uses bounded membership scans for axes of up to eight selections and
lazy maps above that boundary, without per-axis recent-ordinal state. Nonempty
completion uses counted offsets; empty selected-total grids create only missing
cells after duplicate rejection and checked sizing. Repeated selections retain
first-occurrence identity. Small repeated axes use bounded stack spans; larger
repeated axes use proportional arrays. A terminal dictionary resize is capped
only for a new identity when actual stored groups fill the dictionary and exceed
half the representable grain. Capacity comes directly from the dictionary;
sparse inputs do not trigger eager Cartesian reservation.
The [consumer performance summary](qualification/query-performance.md#historical-report-construction-and-materialization)
separates actual `ReadAsync`, isolated materialization and expression-construction
observations. Allocation savings do not imply lower CPU or reader latency;
record smaller asynchronous and adverse native cases as well as large grids.
The fixed unit budgets remain distinct from native consumer work allowances.

For invariant long Doka paths, invalid children are grouped into coordinate
blocks independently of each outer occurrence. A child is invalid when it lacks
its visible, allowed, consecutive direct parent. In a valid nested set, such a
child blocks the upper-to-lower path exactly when its Left is in
`(upper.Left, lower.Left]` and its Right reaches `lower.Right`. A nullable MAX
over those Right values proves existence without a reserved key or coordinate.
The upper endpoint's own incoming edge is excluded; the lower endpoint is included.

The block width is `1 + floor(sqrt(maximum visible Right))`, computed in the
same SQL statement. Integer modulo defines exact block starts without an
overflow-prone end-coordinate addition. Complete interior blocks need only their
maximum Right; the two endpoint blocks retain the exact Left predicate. Summary
joins use mapped Scope/Tree identity, and fringe joins additionally use the
endpoint's block. Unscoped models omit Scope entirely. Original caller rows and
payload multiplicity are never grouped or deduplicated by this internal proof.

Three independent grouped sources avoid correlated rematerialization. Successful
long paths can therefore build the visible failure relation three times, a real
preprocessing and temporary-storage tradeoff. With dense valid coordinates, the
subsequent proof inspects block summaries and at most two blocks of points per
occurrence, rather than all `B` failures for every occurrence. Its partition work
is proportional to `R * (M / W + W)`, where `M` is the maximum visible coordinate
and `W` the derived width, in addition to the constant number of population
passes. `M` is not the visible-row count: filters or a much larger neighboring
tree can make a small tree's fringe wider. The width is a cost estimate; floating
square-root rounding does not change the exact integer partition semantics.

Row-dependent rules retain their direct-edge count; they cannot share a failure
set independent of application input. These proofs assume valid stored bounds
and depths and do not replace arbitrary-corruption validation. Native checks
cover dense on-path/off-path failures, rejecting fringes, clean paths and disjoint
branches. Iterator tuples and temporary relations are observed separately from
elapsed time; no claim of zero server memory or universal latency follows.

Independent nonrelational Doka Nearest candidates use the same coordinate
partitioning on both Left and Right. Two independent exact point relations
retain candidates in the lower endpoint's Left block or Right block. Both
fringes test strict containment. A third independent relation groups complete
Left/Right block pairs and retains their maximum Left; only pairs strictly
before the lower Left block and strictly after its Right block qualify.
These three regions cover every strict-containing candidate.

Candidates containing one lower endpoint form an ancestor chain. A candidate
in its Left fringe is nearer than every candidate in an earlier Left block.
A candidate in its Right fringe is nearer than every interior candidate,
whose Right belongs to a strictly later block. The exact nearest coordinate
is therefore `leftFringeMax ?? rightFringeMax ?? interiorMax`. NULL represents
absence; no key or coordinate is reserved. Native scope/tree/block joins
resolve the fringes, and a scope/tree/Left lookup resolves the actual owner.
Value and complete owner-to-lower path decisions run after coordinate selection;
neither rejection retries another owner. No caller anchor limits that selection.

The two-coordinate summary is not a quadratic block grid on valid laminar
bounds. Intervals are noncrossing chords; contracting consecutive coordinate
blocks preserves outerplanarity. With `K >= 2` blocks per tree there are at most
`2*K - 3` distinct non-loop block pairs and `K` same-block pairs. With one
block there is at most one pair. Each exact fringe has at most `W` candidate
coordinates. The logical lookup work per invocation is thus proportional to
`1 + M/W + W`, in addition to candidate preprocessing. Native plans must still
qualify grouping, temporary indexes and actual invocations: an optimizer can
evaluate a predicate before the caller's other filters. This is not a physical
read or total server-memory bound.

The candidate relations do not copy or group the caller's source. Upstream
filtering, paging, random ordering and duplicate occurrences remain on that
source. Dense controls include empty Left fringes, independent Right-fringe
and interior winners, rejection without farther fallback, and full forests.

These independent grouped candidate relations add preprocessing passes and
temporary storage, like the failure proof. They avoid scanning every candidate
for every lower endpoint on the qualified sparse and dense profiles. A visible
self candidate still takes precedence. Row-aware and query-bearing candidates
retain their correlated mapped Left DESC selection and projected value/path
decision. On valid ancestors that ordering agrees with structural depth.
An index-compatible expression alone does not prove which index the optimizer
chooses, when it evaluates projections, or how many subplan loops execute.

Native counters have different boundaries. MySQL TREE reports emitted iterator
rows times loops, supplemented by a repeated whole-prefix access guard.
MariaDB uses `r_index_rows` before index checks where available and retains
`r_rows` after those checks separately, each multiplied by its execution count.
See [MariaDB's counter definitions](https://mariadb.com/docs/server/reference/sql-statements/administrative-sql-statements/analyze-and-explain-statements/analyze-interpreting-rows-and-filtered-members).
A physical table scan may read foreign decoys once; it is distinct from
materializing those rows into an authorized failure set or repeating the scan
per occurrence. Native work fixtures therefore record both stored and visible
populations. MariaDB's terminal sort above a grouped temporary table can expose
its output; other shapes remain explicitly unknown. Its sort output is a
rounded average per invocation, not peak memory, as defined by the
[11.8.9 implementation](https://github.com/MariaDB/server/blob/mariadb-11.8.9/sql/sql_analyze_stmt.cc#L58-L62).

Inspect both selective and broad native plans. A materialized structural pair
relation can still have `anchors * descendants` rows before a payload join;
predicate pushdown on one medium profile does not establish pushdown for deep
overlap or another provider. Retained plan row counts, loops, temporary
relations and allocations qualify that tradeoff. Scalar results and zero CLR
entities are not claims of zero server memory or constant database work.

The [execution qualification](query-composition-regression-coverage.md#execution-qualification)
distinguishes source coverage, the recorded execution boundary and remaining
native-platform work. Timings describe their measured environment; later
physical expressions require new evidence.

The [recorded complete-report allocation profile](qualification/query-performance.md#historical-complete-report-phase-attribution)
separates disjoint sampled parameter/cache/execution stack attribution from
process-level complete-reader allocated bytes. Both mechanisms retain their
own units and workload boundaries. The completed serial A-B-B-A matrix separately covers actual construction,
native complete reports on both engines and dense materialization. It retains
all process means, mixed materializer/cold observations and normal host-load
limits for its frozen source. It does not qualify subsequent physical query
changes; neither lower builders nor overlapping intervals prove universal
latency or regression freedom. Sixteen native diagnostic traces attribute
sampled allocation stacks, while process-level bytes and native work remain
independent observations. These optional timings introduce no CI/RC gate.

## Regression diagnosis

Actual-plan replay is a separate instrumented execution. The shared
`QueryPlanTestSupport.ExplainAsync` uses an explicit context command timeout
when configured; otherwise its raw diagnostic command has a 120-second
watchdog. It does not change EF's ordinary query timeout, result or native-work
budgets. A streamed result can finish while an instrumented execution exceeds
the ordinary first-response deadline, so replay duration is recorded separately.
MySQL replay also reads `SHOW WARNINGS` and rejects warning 1317: MySQL can
return a partial TREE after interruption, as shown by its
[interruption reporting path](https://github.com/mysql/mysql-server/blob/8.4/sql/sql_class.cc#L2038-L2061).
A nonempty plan is insufficient;
the existing complete-root cardinality and work assertions must still pass.
See [MySQL's EXPLAIN documentation](https://dev.mysql.com/doc/refman/8.4/en/explain.html).

Portable long-path proofs count children with one visible allowed direct
parent, retaining native key equality, consecutive depth, containment and
row-dependent rules. For row-independent long-path rules, SQL Server's form
fences the invalid children: their identity and interval points form an
uncorrelated relation behind an `ORDER BY ... OFFSET 0` boundary that references
no caller bound. Each path then asks for one point with
`upper.Left < Left <= lower.Left` and `Right >= lower.Right` through a scalar
`TOP(1)` lookup; in a valid nested set such a point exists exactly when the path
is incomplete. In the measured plans the points were completed once and the
scalar lookup stayed lazy and correlated: an `EXISTS` let SQL Server turn the
proof into an eager probe join beside nearest candidates and rebuild the points
for every row, and the former grouped failure summaries were recomputed for
every fact. MySQL/MariaDB retain their separately qualified
scalar-width form; row-dependent rules retain their exact per-occurrence count.

SQL Server selects the nearest strict ancestor in descending `Left` order.
For the required valid bounds/depth model, ancestors form one nested chain:
greater depth has strictly greater `Left`. Filtering that chain does not alter
this equivalence. The selected owner still precedes its value and complete-path
checks, with no fallback after rejection. This uses the existing coordinate-order
primitive and index order; it removes the separate depth-sort expression, without
guaranteeing a particular native access path. PostgreSQL retains the depth order
of its separately measured broad-payload correction.

Supervisor resolution separates missing/ambiguous origin markers from complete
unique-person evidence with `UNION ALL`. The evidence relation retains its
independent `DISTINCT` boundary and joins the grouped unique-person witnesses
through an inner join. The server still decides ambiguity, and an admitted
person retains every occurrence on that origin's path. No additional status
row is sent for a successful origin and the reader count is unchanged.

SQL Server's strict ancestor strategy drives each visible nonleaf owner into its
own source interval. A correlated `CROSS APPLY` selects the caller's sources
inside the owner's `Left` range, ordered by `Left` behind `OFFSET 0`. Strict
ancestors have no bounded range in any coordinate index, while each owner's
descendants form one `Left` range. In the measured plans the boundary kept the
apply correlated, so a plan compiled for one source no longer scanned every owner
again for each later source.
Owner and source remain mapped entities: owned auto-includes, collection
identifiers, caller occurrences and equal-bound owners are preserved without a
global owner window. Inclusive ancestors whose selection is an independent fact
query fence that selection behind a node-key `OFFSET 0` boundary, which the
measured plans completed once; SQL Server otherwise evaluated the fact `EXISTS` for every node of each selected anchor
subtree. Hierarchy-backed selections remain correlatable for later direction
changes. These SQL shapes do not prove physical seeks; native plans and the
unchanged capacity guards remain the acceptance criterion.

### SQL Server composition plans

The six public composition operations tag their SQL Server queries with the EF
tag `Doka.EntityFrameworkCore.NestedSet composition`. The adapter's command
interceptor appends `OPTION (OPTIMIZE FOR UNKNOWN, MAXDOP 1)` to reader, scalar
and non-query commands whose leading EF tag block carries that tag, once per
statement and before an optional terminator. Ordinary hierarchy reads,
mutations, untagged application queries and other adapters are unchanged.

SQL Server otherwise reuses the plan compiled for the first parameter values.
A plan built for one selected source repeated subtree and path work for every
later source, and fresh compiles still chose that per-row shape for small exact
estimates. `OPTIMIZE FOR UNKNOWN` plans with average selectivity instead of the
first sniffed values; in the measured capacity profiles this kept each
composition operator within its population-pass budget. Parallel nested loops
rebuilt an uncorrelated inner relation once per worker thread, so completed path
failures repeated with the core count; `MAXDOP 1` removes that per-worker
repetition. Neither hint fixes a physical plan or guarantees one-time
evaluation; the actual-work guards below remain the acceptance criterion.
Composition queries therefore give up parallel scans. Applications must not
append a second `OPTION` clause to tagged composition commands.

The mixed-direction, deep-path and supervisor capacity profiles additionally
read SQL Server's actual Showplan counters. They sum `ActualRows` and reported
`ActualRowsRead` across each operator's threads, without multiplying by
`ActualExecutions`: these SQL Server counters are already cumulative. The
existing `ForPaths` allowance admits one population validation plus independently
required selected path evidence. Nearest-self checks require no edges, strict
nearest-parent checks require one, and full root paths use their exact chain
lengths. Correct final aggregates cannot conceal repeated population reads.
The [native counter controls](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/CompositionSqlServerPlanWorkTests.cs)
include the observed 95,050-output/10,000,100-read adverse plan and malformed,
empty, parallel-thread and estimated-only evidence. These guards do not
establish that a changed SQL Server query has passed native execution.

PostgreSQL's nearest decision uses one depth-ordered owner projection rather
than two scalar-key selections and a subsequent owner lookup. This preserves
the original nearest-before-value/path contract. The
[successor qualification](qualification/query-composition.md#successor-ci-and-postgresql-broad-payload-correction)
records a reproduced quadratic broad-payload join and its corrected actual
operator work. An inactive branch can still affect optimizer costs; command
count, zero materialized entities and a small final aggregate do not detect
repeated fact scans. Native work guards therefore count emitted tuples and
rows rejected by join filters, multiplied by actual loops.

SQL Server fixture statistics are read from `sys.stats` for the exact mapped
table through a parameterized `OBJECT_ID` lookup. Each returned statistic is
updated with `FULLSCAN` in its own command, including automatic column
statistics. This avoids accumulating all independent refreshes under one
command deadline without sampling or changing the deadline. An individual
large scan can still exceed that deadline and must fail visibly. The
[Microsoft statement contract](https://learn.microsoft.com/en-us/sql/t-sql/statements/update-statistics-transact-sql)
defines the named-statistic and all-table forms. This is test setup outside the
measured query; it adds no library-managed production statistics updates.

1. Reproduce on the same controlled environment and stable source.
2. Compare package DLL hashes, runtime, engine/image, schema, application indexes,
   statistics, topology and selected occurrence counts.
3. Separate construction, parameter extraction, server execution, transport,
   materialization and complete-reader measurements.
4. Inspect command counts, actual plans, repeated loops, temporary cardinality
   and lock waits before interpreting elapsed time.
5. Confirm the full result bag, payload and hierarchy state with an independent
   oracle; a partial read or incomplete plan is not a qualified timing pair.
6. Repeat paired observations and retain every adverse or unknown result.

The feature summaries cover [query/native work](qualification/query-composition.md),
[managed/consumer costs](qualification/query-performance.md),
[provider and migration inputs](qualification/provider-migrations.md) and
[Unicode identities](qualification/unicode-identities.md). They describe their
exact dated inputs. Historical numbers do not qualify a later binary, another
published package, a different platform or a hosted release.

Do not weaken locks, visibility, validation, limits or required indexes to hide
an unexplained timing or allocation change.

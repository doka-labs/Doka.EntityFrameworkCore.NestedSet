# Performance and capacity

Nested sets exchange write amplification for fast ordered hierarchy reads. The
package keeps client work iterative and bounded, while database range updates
remain proportional to the affected coordinate suffix in the worst case.

## Cost model

Let `n` be the node count of one TreeId and `s` the size of a selected subtree.

| Operation | Dominant work |
| --- | --- |
| Tree read | Indexed `(Scope?, TreeId, Left)` range, proportional to the result |
| Children | Indexed `(Scope?, TreeId, ParentId, Position)` range |
| Ancestors | Tree-local containment predicates, proportional to depth plus plan cost |
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

The [benchmark project](../benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks/README.md)
uses BenchmarkDotNet 0.15.8 for explicitly selected measurements in isolated
.NET 10 Release processes. Developers run it before and after an optimization
or when investigating behavior. Its results impose no performance threshold,
required CI/RC run, or automated merge decision. The normal regression projects
retain their existing deterministic structural and allocation contracts.

List the catalog or select a feature and its datasets:

```sh
dotnet run --project benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks \
  -c Release -- --list flat

dotnet run --project benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks \
  -c Release -- --engine MariaDb --nodes 1000,10000 --shape Wide,Balanced \
  --trees 4 --tracked 0,1000 --filter '*MoveBenchmarks*' \
  --artifacts /tmp/nestedset-move-MariaDb
```

Measurement and diagnostic modes share BenchmarkDotNet's option aliases,
framework response files, and case-filter semantics, including parameter values.
Pass provider/dataset options directly to the launcher. Framework-selected
artifacts contain results and provenance together. Diagnostics execute each
selected method/dataset once even across multiple measurement jobs.
Execution requires an explicit native selector, such as `--filter '*'`, a
method filter, or a category/attribute selector. Executing options without a
selector show usage and the catalog, then exit with code 2 before resources or
artifacts. No arguments, help, and listing still show information without a run.

Feature folders cover Core, Queries, Insert, Move, Delete, BulkImport,
Ordering/SaveChanges, Validation, and Rebuild. Input generation is deterministic
and iterative for wide roots, deep chains, and balanced binary trees. Sizes such
as 100, 1,000, 10,000, and 100,000 are selected explicitly. `--nodes` describes
the total forest size and `--trees` its independent coordinate spaces. Basic
option validation checks shapes and integer bounds, including a total size of
at least ten. Before resources are acquired, each selected database case must
have at least ten nodes per effective tree. Cross-tree moves use at least two
trees even with `--trees 1`, and validate and report that effective count.
Unselected combinations do not undergo the per-tree check. An unmatched native
selection or a Core-only diagnostic selection exits with code 2 before resources
or artifacts. `--tracked` controls unrelated clean EF entries. Seeded
nodes carry a 1,024-character payload. The chosen topology determines whether
a branch operation moves or deletes a small or large part of its tree. Ordering
rename permutes siblings in wide/balanced trees; a deep chain measures payload
plus coordinated-save work because it has no sibling group.
`--filter '*Ordering*'` selects both `OrderingBenchmarks.SortedInsert` and
`OrderingSaveBenchmarks.RenameAndSave`; insertion has no tracked hierarchy
preload, while the rename prepares its tracked node before measurement.

MySQL and MariaDB use Doka; PostgreSQL, SQL Server, in-memory SQLite, and file
SQLite remain distinct profiles. Server databases use owned Testcontainers with
the existing digest-pinned images, two CPU and 2 GiB limits, and no application
database credentials. Startup and disposal occur in the launcher, outside the
measured process. Database pooling is disabled. Embedded SQLite results must be
identified by memory or file mode. Database tests and other provider runs can
compete for the same host resources, so collect comparisons sequentially.
Core-only measurements acquire no database owner even with a server engine
configured, and their provenance records `database: null`. The engine setting
does not imply that a database was observed.

Database benchmarks execute one awaited operation per iteration, restoring
state and tracker before every observation and checking the actual effect
afterwards. The default .NET 10 throughput job uses three warmup and twelve
target iterations with invocation count and unroll factor one. Core operations
use BenchmarkDotNet's normal throughput calibration. Framework job overrides
remain available within those isolation and state invariants. A `--job dry` run
checks execution and export; it does not establish comparative performance.

Global setup constructs the fixture synchronously without database I/O. Global
cleanup and workloads remain asynchronous. The pinned framework's iteration
hooks bind to `System.Action` without `AwaitHelper`, so Task-returning iteration
hooks cannot bind; void wrappers fully complete asynchronous preparation and
verification helpers outside the observation window.

Container startup, schema creation, seeding, tracker preparation, validation,
and cleanup are outside timing. BenchmarkDotNet 0.15.8 takes managed allocation
snapshots inside the iteration setup/cleanup boundary, including its extra
diagnostic workload. On .NET 10, process-wide allocated-byte counts include
asynchronous continuations and can also include background client allocations.
Prepared data still affects the heap and GC state. Allocated bytes and GC counts
are distinct from retained or peak memory and from database-server memory;
those require their own profiling. The tagged engine is the source for these
measurement boundaries, as recorded in
[D-015](decisions/D-015-benchmarkdotnet-development-measurements.md#sources).

Inspect SQL and writes in a separate untimed run. This mode supports Debug and
Release for functional diagnosis and breakpoints. Actual measurements require
Release; a Debug launcher rejects them before acquiring resources or artifacts:

```sh
dotnet run --project benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks \
  -c Release -- --diagnostics --engine SqliteMemory --nodes 10000 \
  --shape Balanced --trees 4 --filter '*InsertBenchmarks*' \
  --artifacts /tmp/nestedset-insert-SqliteMemory-diagnostics
```

The same operation and effect assertions produce `command-diagnostics.json`.
Preparation commands and verification reads are excluded. All completed EF SQL
commands contribute to the command count. `HierarchyUpdates` and `AffectedRows`
include only completed NonQuery commands whose SQL starts with `UPDATE` and
names `BenchmarkNodes`. Result-producing payload UPDATE commands observed as
readers, including `RETURNING`/`OUTPUT`, contribute to the command count but
are absent from these UPDATE/row observations. The fields do not report all
hierarchy writes or a complete write-amplification budget.

The diagnostic run has no manual timer, sampler, statistical measurement, or
acceptance budget. Lock statements count as commands; transaction API calls,
lock-table updates, and deletes are excluded from the UPDATE/row fields.
Repeated qualifying updates count repeatedly; affected rows are not physical
disk or WAL writes.

Markdown, full JSON, and raw CSV preserve BenchmarkDotNet results. A
credential-free `provenance.json` preserves the initial commit, dirty state and
working-source fingerprint as `source`, and records the final identity as
`endSource`. `sourcesChangedDuringRun` reports differences between those
snapshots; the final state does not replace the initial identity. The sidecar
also adds SDK/runtime, package versions, CPU/OS, database version/image,
container resources, dataset settings, and actual jobs and observation counts.
Invalid scenarios, failed operations, and missing measurements fail visibly;
slow observations are reported without a threshold verdict.

Compare source revisions on the same controlled host with identical runtime,
provider, images, resource limits, datasets, and jobs. Preserve the raw outputs
and source identity, inspect the distribution, and repeat paired runs. If
`sourcesChangedDuringRun` is true, repeat with a stable source state. A hardware
or configuration change creates a new comparison series. Historical
handwritten-runner observations use different measurement boundaries and do
not establish a BenchmarkDotNet baseline. Dated regression-test allocation
observations elsewhere in this guide retain their stated scope.

The [benchmark regression project](../tests/Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests/Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests.csproj)
belongs to `tests` and runs in both Rider's normal Debug configuration and Release:

```sh
dotnet run --project tests/Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests -c Debug
dotnet run --project tests/Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests -c Release
```

The Debug suite also checks rejection of actual measurements before database
ownership or artifact creation.

## Regression diagnosis

1. Reproduce on the same controlled environment.
2. Compare source, dependency locks, provider, engine, schema, indexes,
   statistics, and data shape.
3. Inspect command count and affected rows before elapsed time.
4. Inspect the actual execution plan and lock waits.
5. Confirm identical final hierarchy and payload state.
6. Repeat paired samples to distinguish noise from persistent change.

Do not weaken tree locks, skip validation, or remove required indexes to hide an
unexplained timing change.

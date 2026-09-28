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

The [scale regression](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Concurrency/NativeTrackedIdentityGuardTests.Scale.cs)
owns the assertion and warm-baseline setup. Existing unrelated-tracker and
forest-insertion allocation ceilings remain separate contracts.

Bulk planning and full rebuild use iterative compact buffers and bounded
64-entity write batches. The qualification target is at most 512 MiB additional
managed heap for one million planned nodes. Application payload size and an
already populated change tracker remain outside that library-only budget.
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

## Qualification targets

| Area | Target |
| --- | --- |
| Correct tree size | 10 million nodes readable and fully validatable |
| Width | 1 million direct children without recursive client traversal |
| Depth | 100,000 levels without call-stack recursion |
| Bulk import | 1 million nodes in one atomic batched operation |
| Bulk/rebuild heap | At most 512 MiB additional managed heap for 1 million nodes |
| Anchor query | One SQL command without loading the anchor entity first |
| Normal mutation | Command count independent of node count, width, and depth |
| Independent writers | At least 64 writers on different trees without a global library lock |
| Hot tree | 64 writers serialize without deadlock or structural damage |
| Cross-tree move | Stable database lock order for opposing moves |

These are release qualification targets, not latency guarantees for arbitrary
hardware. Repository tests enforce deterministic width, depth, planning,
command-count, affected-row, and concurrency properties.
The [regression matrix](regression-coverage.md) names the positive, negative,
and adversarial mechanisms behind these checks. Neither its entries nor these
targets imply universal regression coverage or certification of a particular
hardware throughput.

## Deterministic regression measurements

Shared measurement specifications live in the non-runnable
specification library. Concrete provider projects supply their engine
fixtures when added; provider-local measurements stay with those owners.
Executing the shared library alone does not run these specifications.

Assert final structure, SQL command and update counts, affected rows,
query shape, iterative planning, and bounded allocation form. Hardware
timing and process-wide observations are separate workload evidence.

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

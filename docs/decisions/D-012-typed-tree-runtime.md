---
id: D-012
status: implemented
date: 2026-09-26
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Typed tree identity, feature dispatch, and exact-tree execution inside the EF Core package"
supersedes: []
superseded-by: []
amends: [D-005, D-011]
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-012 -- Carry typed tree identities through feature execution

## Context and Problem Statement

The public facade distinguishes independent trees through a mandatory TreeId
and an optional Scope. Before this decision, its internal path delegated to
`NestedSetService<TEntity, TKey, TScope>`, which also supported scope-wide
forests. The service erased TreeId to `object`, selected behavior through
nullable constructor arguments, and constructed operations unrelated to the
requested feature. Storage and transaction execution therefore retained two
different identity contracts behind one public API.

Tests exercised this compatibility path directly, including several roots
sharing one coordinate system. Keeping those tests green did not establish
that the internal design expressed the public one-root-per-tree contract.
The question is how to remove that alternate runtime completely while
preserving valid algorithms, provider behavior, transaction ownership, and
the application's existing public entry points.

## Decision Drivers

- Make complete tree identity mandatory in structural storage and execution.
- Preserve optional Scope without requiring a scope property or another
  context property for an unscoped hierarchy.
- Keep NodeKey, Scope, and TreeId typed after model-based dispatch.
- Avoid repeated generic reflection, unused feature objects, and copied
  forest request arrays on the operation path.
- Keep caller transactions, rollback restoration, deterministic tree-lock
  order, and consistent read snapshots explicit and independently testable.
- Preserve shared provider and relational primitives without another package,
  dependency, handler framework, or public service abstraction.

## Considered Options

- Typed feature execution with one exact-tree store
- Add a TreeId type parameter to the compatibility service
- Keep one erased identity strategy throughout the runtime

## Decision Outcome

Chosen option: "Typed feature execution with one exact-tree store", because
the public contract already determines the configured identity types and
selected tree. The implementation can retain that information throughout
each feature instead of recreating it after a compatibility boundary.
This is the repository's architectural choice; EF Core does not prescribe this
facade or the number of internal generic parameters.

The public facade validates caller argument types against finalized entity
metadata, then dispatches through
`NestedSetMutationInvoker<TEntity, TKey, TTreeId, TScope>`. Invokers are
immutable and cached by `IEntityType`. A weak-table entry holds a `Lazy` with
`ExecutionAndPublication`, so concurrent cache misses can create competing
wrappers without repeatedly closing and constructing the winning invoker.
No cached invoker retains a context, scoped value, transaction, or operation
instance.

The role order is Entity, NodeKey, TreeId, Scope throughout four-role runtime
types. Facade methods retain the caller's generic scalar types and use generic
virtual dispatch. After role validation, each override calls the requested
closed specialization's static feature core directly. Those cores are
stateless; no invoker cast or per-operation adapter is needed. NodeKey,
TreeId, and Scope scalar values do not pass through `object` between facade
and structural feature execution.

Binding Scope creates one private `ScopeValue<TScope>` carrier containing the
independent typed snapshot. The binding retains a carrier reference rather
than an `object` scalar. The closed invoker reads it through an exact carrier
reference cast, so operation construction does not box and recover the Scope
value. Unscoped bindings need no carrier. This state belongs to the facade
binding, never to the model-lifetime invoker cache.

Each operation creates an exact
`NestedSetStore<TEntity, TKey, TTreeId, TScope>` with one required constructor:
`(context, entityType, scope, treeId)`. The finalized entity type and mapped
TreeId type are checked, and identities are captured using their EF value
comparers. NodeKey arguments are also snapshotted before the first await.
Configured snapshots preserve mutable converted types; binary identities
receive one defensive array copy even if an application comparer is shallow.
Sentinel and provider equality remain separate from snapshot ownership.
Structural queries always constrain TreeId and constrain Scope
when configured. The internal `NestedSetNoScope` marker closes shared generic
code for unscoped models; it creates no column or public scope requirement.

The invoker constructs only the requested feature and its consumed helpers.
Feature algorithms receive the typed store. There is no scope-wide store,
`IsTreeBound` branch, compatibility identity resolver, or alternate service.
Forest import passes the validated typed import list directly to its planner
without an intermediate array of erased requests.

Managed-save groups convert their heterogeneous pending entries to typed
`EntityEntry<TEntity>` wrappers once when constructing the closed group.
Their cached factory is keyed by the exact configured hierarchy owner and
closes `TEntity` to that owner's CLR type. Mixed derived entries share its
Parent, ordering, lock, and refresh plan. Neither the concrete changed subtype
nor an unconditional EF inheritance-root lookup defines this boundary.
Multi-role NodeKey, Scope, TreeId, and structural reads use the public
`PropertyValues.GetValue<TValue>(IProperty)` API. A guard candidate retains one
live `CurrentValues` wrapper, and structural checks reuse one wrapper for
multiple reads from an entry. The wrapper reflects subsequent current-value
changes; it is not a cloned snapshot. Independently owned identity snapshots
remain necessary for late verification and rollback.

Parent reads use an immutable per-key adapter that selects the mapped nullable
value type or nullable reference representation without per-row reflection or
an intermediate object-valued property getter. Presence remains separate from
an assigned default key. Reference null checks use identity rather than domain
equality operators, so an overloaded operator cannot hide a present parent.
No adapter caches a context, model, or tracked entry,
and ordinary reads do not cast entry infrastructure to an internal EF type.
EF still allocates the reused values wrapper, shadow storage may already contain
boxed values, and setters or non-generic metadata interfaces retain their
framework boundary. Its constructor also allocates complex-property lists in
the pinned EF version. Single-role reads therefore retain the public typed
`PropertyEntry.CurrentValue` getter instead of creating a values wrapper for
one value. Wrapper reuse is selected by the concrete consumer, not applied as
a universal allocation optimization. Neither typed read API guarantees
allocation-free behavior across every mapping.

The public `IUpdateEntry.GetCurrentValue<TProperty>(IPropertyBase)` contract
is a supported alternative for EF extensions. Given an update entry, it reads
the exact mapped model type without constructing a property wrapper. The
public same-context acquisition route is
`context.GetService<IUpdateAdapterFactory>().Create().Entries`;
`CreateStandalone()` owns another tracker and is not interchangeable.
Managed-save groups no longer retain a same-context adapter solely for token
refresh: the shared refresh path uses the exact tracked entry already owned by
the caller. Insertion identity cleanup separately requires that entry's native
identity and reuses its public typed current-value contract for comparison.
The current readers retain their existing `EntityEntry` processing paths:
changing those paths must account for how matching update entries are acquired,
associated with exact named mappings, and retained for live and late checks.
Adapter creation or key-array lookups can allocate; neither is necessary when
the matching update entry is already available. The update-entry route remains
a supported, unselected option for the current readers. A net gain must be
measured across entry acquisition and repeated reads, including the guard's
complete lifetime.

Public query bindings use the same configured typed snapshot helper. An
`InTree` binding owns one TreeId snapshot shared by its deferred query and
tree facade; the synchronous handoff does not need a second clone of that
already owned value. NodeKey and Scope inputs retain their own query snapshots.
Store and lock-request snapshots remain independent ownership boundaries
because operations and requests can outlive or be reused independently of
the query binding. A context-scoped store also reuses its immutable identity
query instead of rebuilding Scope and TreeId predicates on each access.

Parent values use `NestedSetParent<TKey>` with separate `HasValue` and `Value`
fields. This represents both nullable value-type and reference-type Parent
properties without boxing each structural row. A present parent key of `0`
or `Guid.Empty` is not an absent parent. Queries, placement, structural writes,
ordering, maintenance, and active single/bulk insertion stages consume this
typed carrier. The cached setter bridge adapts it to the mapped nullable CLR
property only at EF's property-expression boundary. Mapped equality uses
translatable typed equality without requiring a custom key struct to declare
`==` or `!=` operators.

EF inheritance keys belong to the inheritance root metadata. When a scoped
concrete hierarchy needs a missing `(Scope, NodeKey)` parent alternate key,
finalization creates that key through the root builder with the same inherited
properties. The concrete hierarchy remains the parent relationship's principal
entity type; its restrictive deletion behavior and existing scalar primary key
are preserved. This separates EF key ownership from the concrete physical
table's identity and collation rather than redefining the user's inheritance.

The actual mapped Parent CLR type determines nullable property access. A
generic nullable annotation alone does not turn an arbitrary value-type key
into `Nullable<TKey>`. An already nullable CLR key must not receive another
Nullable wrapper, even when EF marks its database column required.
Expression construction therefore preserves the mapped type and uses
translatable `Equals` for operator-free converted keys. Any
necessary non-generic equality-expression fallback remains at translation
construction; retained structural and staged keys do not become erased state.

Active staged NodeKey, Scope, TreeId, and Parent values have their runtime
types and independent configured snapshots. `NestedSetBulkPlan.OriginalStructure`
is a different contract: it stores sparse, raw metadata-shaped values needed
to restore detached inputs, including null and mapped sentinels that are not
valid active identities. `ParentChange.OriginalParent` likewise preserves the
exact original tracked property. Keeping those rollback values erased avoids
turning an invalid or unset input into a valid runtime identity during restore.
Managed-save refresh projections also remain heterogeneous: they combine
structural values with application-configured concurrency properties whose
types are known only from metadata. Their bounded result rows and public EF
property assignments are deliberate translation/materialization boundaries,
not another erased identity model retained by feature state.

Effective identity collations belong to the configured hierarchy and its
exact structural store. Resolution uses the physical column's collation,
including shared-column inheritance, before Doka's canonical collation on the
mapped physical table owners, then the model collation. Only the absence of
all applicable metadata means that the database default is unknown. Table
collation support is enabled through explicit provider capability wiring;
private Doka migration annotations are not an extension contract.

Applicable table owners follow EF's physical mappings, including explicit
split fragments. TPT traversal retains the original mapped type while visiting
ancestor tables; TPH and TPC stop at their own mappings. A leaf declaration can
therefore be an applicable Doka table facet for the ancestor structural store.
Looking only at the configured CLR entity's current table would lose that
contract.

Contradictory applicable table declarations are a deliberate library validation
boundary. Doka selects the first annotated mapped owner; NestedSet rejects
different declared owner values when an identity needs that table default, rather
than allowing its native identity to depend on mapping visitation order. An
explicit physical-column collation wins before this ambiguity check. This is
not a provider rejection or a restriction on TPT, TPC, table splitting, or
entity splitting as model shapes.

Finalization captures these facets on each hierarchy owner before EF removes
design-time relational metadata. An inherited `IProperty` can map to different
concrete TPC tables, so a property-only capture cannot represent both stores.
Concrete TPT facade bindings resolve the configured ancestor owner rather than
expecting another independent capture on an unconfigured leaf. Independent
concrete TPC hierarchies retain their own captures and cannot share a facet
merely because their property object is the same.
Registry identity columns, native rowset deduplication, and lock-request
ordering consume the same effective comparison facts. A known table collation
must not be ignored while creating a registry with the database default,
because that can merge independent source Scopes sharing one TreeId.
Captured runtime resolution reads the owner facets without a schema lookup or
per-operation metadata scan.

Each hierarchy owner stores one string facet for each configured string-backed
identity property. An empty string records a completed capture whose database
default remains unknown; a missing facet means incomplete capture and must
not be interpreted as that default. The owner annotations are serialized into
generated compiled models.
Older supplied or compiled models with incomplete capture must be regenerated
with the current NestedSet conventions. They receive an actionable rejection
rather than silently substituting another collation or rebuilding a model.

Public anchor queries join against the visible anchor and keep TreeId and
boundary predicates available to the database planner. Direct parent and child
queries join ParentId and NodeKey with the principal key's native comparison.
Bound scalar identities use the same typed scalar equality primitive as
structural storage. Metadata-only anchor joins remain a translation boundary:
their expressions must preserve the actual physical mapping, nullable Parent
shape, and native collation rather than assume one runtime key representation.
That expression boundary is not retained erased identity state.
The direct LINQ anchor joins retain their `EF.Property<object>` selectors.
EF binds those selectors by entity and property name to the mapped column;
it does not execute them as CLR object getters. Exact metadata-typed selectors
are possible through `NestedSetExpressions.Property`, but replacing that
access alone does not compose the joins, align nullable Parent and principal
key types, or preserve operator-free converted equality and principal-key
collation. The current choice keeps each join and boundary predicate explicit.
Metadata-typed composition remains a supported option and can be introduced
without a per-operation generic cache.
The chosen `SelectMany` shape references the outer row in its `Where` predicate,
which EF Core translates to a relational join; it does not require `APPLY` on
SQLite. Confirmation inspects actual provider SQL and query plans rather than
treating LINQ syntax alone as a performance guarantee.

`NestedSetMutationExecutor<TEntity, TKey, TTreeId, TScope>` retains the same
closed types and accepts only requests for its exact finalized entity mapping.
It checks known affected tracked identities before SQL using definite provider
representation equality. A lazy set indexes complete `(Scope, TreeId)` pairs
with provider-representation comparers resolved once, giving expected
`O(n + r)` capture work for `n` tracked candidates and `r` requests. This is
hash-table algorithm cost, not a constant-time guarantee for arbitrary custom
converters or adversarial hash collisions. Candidate values retain one live
current-values wrapper and independent snapshots for late verification.
The private candidate buffer appends fixed blocks of 64 value records rather
than repeatedly copying a growing array of records and entry references.
Only its small block directory grows; no pool or global cache retains those
application-owned references after the guard's lifetime.
Unequal representations may still be native aliases, so unresolved tracked
NodeKeys and requested trees are tested together in database predicates after
all locks are held. Qualified native keys use a provider-capability-gated
collection parameter per Scope group. Each query contains at most 64 complete
requested tree identities. Other keys are packed in at most 768 candidates
before Scope grouping, so many small Scopes share one bounded predicate rather
than issuing one probe per Scope. Candidate predicates are built once and
reused across request batches. Scope grouping preserves
Scope-and-NodeKey pairs; it does not form independent scope and key sets that
could match a different tenant's row. No per-entry lookup or generic reflection
is needed. Scope groups use provider representation comparison and retain a
native SQL Scope predicate. Native collation aliases may yield additional
groups, but cannot cause an affected row to be omitted. Native key collections
are deduplicated because a Doka rowset join would otherwise repeat matches.
Tracker state and captured identities are rechecked after the membership probes
complete and before structural writes.

Bounded scalar collections explicitly use `EF.MultipleParameters` when their
finalized `IProperty` has a qualified native CLR mapping without a converter.
Every multi-value caller consumes that same metadata-aware policy; the guard
does not maintain another converter rule. This per-query policy overrides an
application's global Constant or Parameter collection setting. It differs
from `EF.Parameter`, which deliberately chooses the qualified native collection
transport. Converted keys retain verified balanced scalar equality predicates,
including converters whose model CLR type is an ordinary integer or string.
This is a conservative qualified library policy, not a claim that EF cannot
translate converted IN expressions. Single-value and intentionally scalar
rowset predicates use the explicit scalar primitive without creating a
collection. Scalar guard packs bound padded candidate and Scope parameters
to 1,536, plus at most 128 for 64 complete requested identities: 1,664 total,
below EF's SQL Server parameter threshold. Qualification must inspect actual
commands and padding as well as successful results.

Scalar membership projects the constant `1` and takes the first row, comparing
the result with `0` for absence. EF translates this to a one-row limit with
the same complete request and Scope/NodeKey predicates. The reason is a
reproduced SQLite boundary: its resolver accumulates nested expression height,
and EF's `Any` translation wraps the predicate in an outer `EXISTS` expression.
That shape exceeded the default depth limit even though the LINQ predicate
was balanced. Reading one constant row preserves the 768-candidate bound and
indexed filters without the additional resolution scope. Native collection
membership retains its qualified `Any` path. This does not promise support
for arbitrary deployment-specific reductions of SQLite's expression limit.

An unaffected tracked tree remains allowed even when an application comparer
equates its TreeId with another stored identity.
A temporary attachment observer also covers registry-lock awaits when the
hierarchy tracker is initially empty. This path checks registered pending
state without adding native queries or another full detection pass.

The candidate Scope comparison has a narrow MySQL translation boundary.
MySQL 8.4.11 reproduced equality folding for a binary-collated Scope column:
`Scope = @requestedScope AND Scope = @candidateScope` returned one matching
row for case-distinct parameter values that identify different stored Scopes.
The same contradiction reproduced in plain SQL without a JSON collection or
an EF query, so this is not evidence of a Doka key-transport defect.

The guard therefore emits an internal typed method marker for candidate Scope
equality for string provider representations on the MySQL provider family,
including MariaDB. A composable `IMethodCallTranslatorPlugin` translates the
marker to `STRCMP(column, candidate) = 0`. The request's ordinary indexed Scope equality
and the existing native key transport remain unchanged. The same candidate
predicate protects collection groups and bounded converted-key fallbacks;
other provider and scalar representations retain their normal translation.
The plugin uses EF's documented Scoped, multiple-registration contract rather
than replacing the provider's translator service.

This function boundary preserves the column's native comparison and also
covers genuinely unknown database defaults. An inherited table collation
recorded in canonical metadata is known and must first be captured for the
registry and rowset consumers; the function is not a substitute for that
metadata parity. When no applicable column, supported table, or model collation
exists, adding a guessed explicit COLLATE clause cannot establish the required
contract. In the pinned MySQL source, both ordinary string equality and STRCMP
use the same collation comparison callback, including its trailing-space
behavior. STRCMP also propagates SQL NULL rather than inventing null equality.
No schema lookup, extra membership command, public configuration, or dependency
is introduced.

Mutation execution receives explicit typed
`NestedSetTreeLockRequest<TTreeId, TScope>` values. Creating a tree
requires the new-identity contract; operating on a tree requires an existing
identity. Tombstones cannot be treated as missing rows. Consistent inspection
uses a separate read-lock path with exact requests. It may preserve pending
tracked changes because it performs no payload save or structural mutation.
Managed saves apply their planned changes under the locks they already own,
without constructing a second mutation executor for each parent change.
Their refresh capture is constructed directly with the known four types and
complete required state after native membership has been resolved. It has no
erased factory or partially initialized constructor mode. The shared lock
coordinator accepts a heterogeneous request view only when it must order
different hierarchy mappings in one application transaction. Typed executors
validate the exact finalized mapping and consume typed request identities;
the coordinator's erased view is not an alternate runtime request model.

`object` remains at deliberate boundaries: EF's non-generic metadata and
snapshot interfaces, sparse original rollback values, heterogeneous refresh
projections, and the registry view ordered using provider mappings. These APIs
do not justify an erased TreeId field in structural storage. EF also exposes
typed property entries and typed `EF.Property<TProperty>` expressions where
the runtime already knows the mapped CLR type.

### Consequences

- Good, because each structural query and mutation carries exactly one tree
  identity and cannot fall back to a scope-wide coordinate system.
- Good, because feature ownership remains visible from dispatch through the
  algorithm, with no duplicate service forwarding or eager unrelated helpers.
- Good, because immutable cached dispatch separates model lifetime from
  context leases and keeps reflection off repeated operation construction.
- Bad, because shared feature types carry another generic parameter, and
  metadata boundaries still need deliberate conversion and comparer handling.
- Bad, because tests of the removed compatibility contract require semantic
  migration to independent trees and child-sibling scenarios. Mechanical
  constructor replacement would preserve invalid expectations.

### Confirmation

Run live-provider cases on each provider project present in this revision; a
filtered run does not establish coverage for a provider introduced later.

- Build `Doka.EntityFrameworkCore.NestedSet.slnx` in Release with warnings as
  errors; expect no compiler or analyzer diagnostics after all source changes.
- Compare public API baselines; no public symbol or mandatory context property
  may be introduced or removed by this internal refactor.
- Inspect storage, feature signatures, and dispatch: TreeId remains typed
  after validation, and an exact entity type and tree identity are required.
  The removed compatibility service and resolver must have no source caller.
- Run unit and every provider integration project. Equal bounds in distinct
  trees must remain isolated; cross-Scope changes, tombstone reuse, mismatched
  argument types, and a second root in one tree must fail before unsafe writes.
- Run transaction, concurrency, and managed-save regressions. Failures must
  restore definitive rollback state, concurrent moves must be revalidated
  under locks, and read-only inspection must preserve pending tracked writes.
- Run allocation and statement-count regressions without relaxing their
  budgets. Large imports must retain bounded batches and iterative traversal.
- Run scalar collection comparisons and sorted bulk refresh with global
  Constant and Parameter modes. Per-query policy must preserve native binary,
  string, and supported scalar equality without hidden collection transport.
  Converted custom keys must retain their verified mapped equality fallback.
- Verify typed tracked reads, mutable query inputs, and named shared managed
  saves. Shadow values, configured snapshots, and rollback state must survive
  without assuming that every EF getter or wrapper is allocation-free.
  Qualify live nullable and reference Parent reads, including misleading domain
  operators, an already nullable CLR key, and temporary sidecars created after
  capturing the values wrapper. Repeated warmed multi-role reads must retain
  the fixed fixture's allocation budget.
- Qualify exact-pair capture against crossed Scope/TreeId combinations and
  provider aliases, then packed scalar membership across candidate 769 and
  requested tree 65. Inspect actual command parameters including padding;
  late affected membership must still reject before structural writes.
- Run the linked regression matrix for assigned default-valued parent keys,
  operator-free converted structs, mutable Parent callbacks, typed request
  lifecycle, named mappings, large native tracked sets, and cancellation during
  native membership probes. Positive and negative cases must preserve inputs,
  tracker ownership, unrelated trees, and caller transaction state.
- Run native and converted-key Scope regressions with identical NodeKeys in
  database-distinct case variants, including an inherited binary table
  collation. An unaffected candidate must remain allowed in one probe;
  affected membership in the second Scope group must still reject in two.
  PAD SPACE aliases must still identify affected rows, while an inherited
  NO PAD collation must preserve distinct trailing-space identities.
- Run public reservations for case-distinct Scopes sharing one TreeId, native
  multi-request locking, explicit column overrides, and TPC physical-store
  capture. Registry and source comparison must agree. Generated compiled
  models must retain those facets; incomplete older captures must reject
  with regeneration guidance. A separate unknown-default guard regression
  must establish its alias through actual database comparison before rejection.
  Compatible TPT owners must resolve the inherited physical store, while
  contradictory applicable declarations must reject unless an explicit
  identity-column collation provides the authoritative comparison.

- Run `eng/validate-adrs.sh`; expect complete profile conformance and
  reciprocal amendment links. These commands are confirmation procedures,
  not claims that a new full qualification run has completed.

## Pros and Cons of the Options

### Typed feature execution with one exact-tree store

- Good, because public type validation is consumed by the entire structural
  path, with only the requested feature constructed per operation.
- Bad, because a complete refactor must update shared algorithms and tests
  together; old forest assumptions cannot remain as an alternate runtime.

### Add a TreeId type parameter to the compatibility service

- Good, because it makes the tree field typed with fewer changes to the
  service's existing callers and forwarding methods.
- Bad, because the duplicate forwarding layer, eager unrelated operations,
  and scope-wide fallback remain. A fourth generic parameter alone does not
  remove the conflicting identity contracts.

### Keep one erased identity strategy throughout the runtime

- Good, because one non-generic strategy can centralize runtime metadata
  dispatch without propagating identity type parameters through features.
- Bad, because repeated casts and runtime checks remain necessary in storage,
  even though validated generic dispatch already supplies the actual types.
  It weakens compiler checking without removing the EF metadata boundary.

## More Information

This record amends the retained facade implementation described in
[D-011](D-011-feature-oriented-source-layout.md) and removes the compatibility
mutation exception described in [D-005](D-005-database-ordering-and-savechanges.md).
It preserves the accepted tree-identity and transaction contracts in
[D-003](D-003-scoped-forests-and-adjacency.md) and
[D-004](D-004-atomic-mutations-and-locks.md).

The [implementation design](../implementation-design.md) shows the runtime
path. Public query composition remains separate from structural mutation
storage: query filters apply to application results, while internal geometry
reads bypass those filters and apply the complete hierarchy identity.
The [regression matrix](../regression-coverage.md) maps these boundaries to
concrete positive, negative, and adversarial tests. It records procedures and
coverage mechanisms rather than claiming an unexecuted qualification result.

### Re-evaluation Triggers

- EF changes the translation of generic `Collate<TProperty>` over converted
  properties, or effective provider-type resolution. Rerun converted
  principal/Parent collation regressions, including asymmetric column
  declarations and nullable model key types, before changing the typed seam.
- EF changes finalized model identity, value-comparer snapshot behavior, or
  model caching; rerun converted-identity and pooled-context regressions.
- A supported deployment requires a dispatch mechanism unavailable to the
  current reflection-based generic initialization; establish that deployment
  contract and qualify an alternative before changing the boundary.
- A new feature needs a real cross-tree store abstraction rather than several
  exact stores; document its identity and locking invariants before adding it.
- Allocation or statement-count regressions exceed existing budgets; trace
  actual consumers before adding another shared execution layer.
- EF changes nullable Parent property translation, collection parameter
  translation, or typed equality for converted values; rerun the Parent and
  native/fallback guard matrix before changing these capability boundaries.
- EF changes typed current-values reads, temporary-value visibility, or the
  lifetime of its values wrapper; rerun nullable, shadow, shared, mutable, and
  allocation regressions before changing the cached Parent adapter or reusing
  another tracked-value representation.
- Profiling identifies material wrapper overhead in repeated structural reads;
  compare the supported update-entry route including acquisition and lifetime,
  and qualify temporary, shadow, named/shared, mutable, and late-change behavior
  before adopting it. An existing adapter can be reused without another factory
  call; the getter alone is not evidence of a net gain.
- A reproducible anchor translation or query-plan failure requires typed
  selectors, or an existing typed join primitive gains real shared consumers;
  reevaluate metadata-typed composition while preserving nullable Parent,
  converted key equality, principal collation, and the anchor-driven join shape.
- EF changes scalar first-row translation, or SQLite changes expression-height
  resolution; rerun the 768-candidate and 64-request boundaries before changing
  the bounded scalar probe shape.
- MySQL changes equality folding, collation coercion, STRCMP, or its
  trailing-space/null behavior, or EF changes composable translator
  registration; rerun explicit and inherited-collation Scope regressions
  before changing the internal native comparison marker.
- EF changes store-specific or shared-column collation resolution, compiled
  annotation serialization, or Doka changes its canonical physical-table
  metadata contract; rerun registry, TPC, and compiled capture parity before
  changing precedence or reusing a property-level cache.

### Decision History

- 2026-09-26: Decision recorded with status proposed.
- 2026-09-26: The owner requested complete removal of the internal compatibility architecture; the exact decision text remains proposed until accepted.
- 2026-09-26: D-005 and D-011 were amended to describe one exact-tree runtime without a retained scope-wide service.
- 2026-09-26: Anchor queries were documented as indexable joins, with EF Core's relational translation source.
- 2026-09-26: Typed execution, configured identity snapshots, native tracked-membership guards, and direct parent-save captures were specified after regression reproduction.
- 2026-09-26: The four-role order, typed Parent and staging pipeline, generic scalar dispatch, typed registry requests, and collection-based native guard were specified with their rollback metadata exceptions.
- 2026-09-26: Generic overrides were aligned with stateless typed feature cores, and a private typed carrier replaced the binding's retained erased Scope snapshot.
- 2026-09-27: Required nullable CLR NodeKeys were covered at the runtime bridge, and invalid null tracked TreeIds were added to the pre-command regression matrix.
- 2026-09-27: A reproduced MySQL 8.4.11 equality-folding boundary was isolated with an internal native Scope translation, preserving provider key transport and native inherited-column comparison.
- 2026-09-27: Known physical-table collations were distinguished from unknown database defaults and captured per hierarchy/store for registry, rowset, and compiled-model parity.
- 2026-09-27: Scoped concrete parent alternate keys were placed on EF inheritance-root metadata while preserving concrete relationship targets and the scalar primary key.
- 2026-09-27: Collection modes, typed tracked/query reads, exact-pair guard capture, and packed scalar membership were refined without removing independent snapshot ownership.
- 2026-09-27: Scalar probes retained their 768-candidate bound with a one-row projection after reproducing SQLite's nested expression-height limit; guard snapshots moved to private fixed-size blocks.
- 2026-09-27: Finalized property metadata centralized converter-aware collection eligibility, while live public current-values reads replaced repeated property wrappers and boxed Parent reads.
- 2026-09-27: Public update-entry reads and metadata-typed anchor composition were evaluated as supported alternatives, with acquisition costs, retained LINQ boundaries, and concrete re-evaluation triggers.

- 2026-09-28: Status changed from proposed to accepted.
- 2026-09-28: The maintainer accepted the current decision and designated the core-maintainers audience.
- 2026-09-28: Status changed from accepted to implemented.
- 2026-09-28: Confirmed typed tree and optional-scope execution, feature dispatch, identity snapshots, provider mapping, and positive and negative runtime regression specifications against the linked repository evidence.
- 2026-10-03: Converted string-backed NodeKeys retain principal collation through generic model-typed Parent expressions; mixed inherited saves bind to the cached configured hierarchy owner rather than concrete entry metadata.
- 2026-10-04: Shared token refresh removed the unused managed-save adapter. Insertion identity comparisons reuse their already required native entry without changing the guard-reader acquisition trade-off.

### Implementation References

- [Public unscoped facade](../../src/Doka.EntityFrameworkCore.NestedSet/NestedSet.cs)
- [Public scoped facade](../../src/Doka.EntityFrameworkCore.NestedSet/ScopedNestedSet.cs)
- [Typed mutation dispatch](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Facade/NestedSetMutationBinding.cs)
- [Public anchor query joins](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Queries/NestedSetQuery.cs)
- [Exact-tree store](../../src/Doka.EntityFrameworkCore.NestedSet/Storage/NestedSetStore.cs)
- [Bounded key collection policy](../../src/Doka.EntityFrameworkCore.NestedSet/Storage/NestedSetKeyFilter.cs)
- [Typed tracked-role reads](../../src/Doka.EntityFrameworkCore.NestedSet/Storage/NestedSetTypedValue.cs)
- [Typed managed-save grouping](../../src/Doka.EntityFrameworkCore.NestedSet/Features/ManagedSave/NestedSetSaveGroup.Core.cs)
- [Effective physical-store identity collations](../../src/Doka.EntityFrameworkCore.NestedSet/Mapping/NestedSetCollations.cs)
- [Inherited parent keys and concrete relationships](../../src/Doka.EntityFrameworkCore.NestedSet/Configuration/NestedSetConstraints.cs)
- [Typed Parent projection](../../src/Doka.EntityFrameworkCore.NestedSet/Storage/NestedSetNode.cs)
- [Structural Parent writes](../../src/Doka.EntityFrameworkCore.NestedSet/Storage/NestedSetStore.Writes.cs)
- [Registry request contracts](../../src/Doka.EntityFrameworkCore.NestedSet/Execution/NestedSetTreeLockRequest.cs)
- [Native tracked-membership guard](../../src/Doka.EntityFrameworkCore.NestedSet/Execution/NestedSetTrackedIdentityGuard.cs)
- [Typed native Scope marker](../../src/Doka.EntityFrameworkCore.NestedSet/Providers/Queries/NestedSetNativeScopeEquality.cs)
- [Composable MySQL Scope translator](../../src/Doka.EntityFrameworkCore.NestedSet/Providers/Queries/NestedSetMySqlMethodCallTranslatorPlugin.cs)
- [Physical Scope collation regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.MySql.Tests/Concurrency/NativeCollationTests.cs)
- [Sparse original bulk rollback values](../../src/Doka.EntityFrameworkCore.NestedSet/Features/BulkImport/NestedSetBulkPlan.Models.cs)
- [Transaction executor](../../src/Doka.EntityFrameworkCore.NestedSet/Execution/NestedSetMutationExecutor.cs)
- [Typed forest import](../../src/Doka.EntityFrameworkCore.NestedSet/Features/BulkImport/NestedSetForestInsert.cs)
- [Public query facade regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Facade)
- [Public mutation facade regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Mutations/Facade)
- [Transaction regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Transactions)
- [Tree-registry concurrency regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Concurrency/TreeRegistry)
- [Ordering and managed-save regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Ordering)
- [Risk and polarity coverage matrix](../regression-coverage.md)

### Sources

- [EF Core 10.0.12 generic Collate translation](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Query/Internal/Translators/CollateTranslator.cs) (primary source; retrieved 2026-10-03)
- [EF Core 10.0.12 converter provider representation](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/Storage/ValueConversion/ValueConverter.cs) (primary source; retrieved 2026-10-03)
- [EF Core typed property queries](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.ef.property?view=efcore-10.0) (primary source; retrieved 2026-09-26)
- [EF Core relational Join and SelectMany translation](https://learn.microsoft.com/en-us/ef/core/querying/complex-query-operators#selectmany) (primary source; retrieved 2026-09-26)
- [EF Core value snapshots](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.changetracking.valuecomparer.snapshot?view=efcore-10.0) (primary source; retrieved 2026-09-26)
- [EF Core mutable value comparers and snapshots](https://learn.microsoft.com/en-us/ef/core/modeling/value-comparers) (primary source; retrieved 2026-09-26)
- [EF Core typed and untyped tracked properties](https://learn.microsoft.com/en-us/ef/core/change-tracking/entity-entries) (primary source; retrieved 2026-09-26)
- [EF Core typed current values](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.changetracking.propertyentry-2.currentvalue?view=efcore-10.0) (primary source; retrieved 2026-09-26)
- [EF Core model cache lifetime](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.infrastructure.modelsource?view=efcore-10.0) (primary source; retrieved 2026-09-26)
- [EF Core pooled context state](https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics#managing-state-in-pooled-contexts) (primary source; retrieved 2026-09-26)
- [Weak-table value factory concurrency](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.conditionalweaktable-2.getvalue?view=net-10.0) (primary source; retrieved 2026-09-26)
- [Lazy initialization concurrency](https://learn.microsoft.com/en-us/dotnet/api/system.threading.lazythreadsafetymode?view=net-10.0) (primary source; retrieved 2026-09-26)
- [Nullable value-type semantics](https://learn.microsoft.com/en-us/dotnet/api/system.nullable-1?view=net-10.0) (primary source; retrieved 2026-09-26)
- [Expression equality operator requirements](https://learn.microsoft.com/en-us/dotnet/api/system.linq.expressions.expression.equal?view=net-10.0) (primary source; retrieved 2026-09-26)
- [EF Core 10.0.12 Equals translation](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Query/Internal/Translators/EqualsTranslator.cs) (primary source; retrieved 2026-09-26)
- [EF Core conversion and null semantics](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions) (primary source; retrieved 2026-09-26)
- [C# generic method contracts](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/generics/generic-methods) (primary source; retrieved 2026-09-26)
- [C# boxing and unboxing](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/types/boxing-and-unboxing) (primary source; retrieved 2026-09-26)
- [EF Core 10 collection translation](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew#improved-translation-for-parameterized-collection) (primary source; retrieved 2026-09-26)
- [EF Core collection parameter modes](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.parametertranslationmode?view=efcore-10.0) (primary source; retrieved 2026-09-26)
- [EF Core 10.0.12 per-query scalar collection marker](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/EFExtensions.cs) (primary source; retrieved 2026-09-27)
- [EF Core 10.0.12 collection-mode precedence](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Query/RelationalQueryableMethodTranslatingExpressionVisitor.cs) (primary source; retrieved 2026-09-27)
- [EF Core 10.0.12 Any translation to an outer EXISTS expression](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Query/RelationalQueryableMethodTranslatingExpressionVisitor.cs#L516-L545) (primary source; retrieved 2026-09-27)
- [EF Core 10.0.12 first-row limit](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Query/RelationalQueryableMethodTranslatingExpressionVisitor.cs#L704-L733) (primary source; retrieved 2026-09-27)
- [EF Core 10.0.12 SQLite limit generation](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Sqlite.Core/Query/Internal/SqliteQuerySqlGenerator.cs#L73-L83) (primary source; retrieved 2026-09-27)
- [SQLite expression-depth limit](https://www.sqlite.org/limits.html#max_expr_depth) (primary source; retrieved 2026-09-27)
- [SQLite 3.53.3 nested select expression height](https://github.com/sqlite/sqlite/blob/version-3.53.3/src/expr.c#L828-L856) (primary source; retrieved 2026-09-27)
- [SQLite 3.53.3 accumulated resolver expression height](https://github.com/sqlite/sqlite/blob/version-3.53.3/src/resolve.c#L2176-L2190) (primary source; retrieved 2026-09-27)
- [EF Core 10.0.12 typed tracked getter](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/ChangeTracking/PropertyEntry%60.cs) (primary source; retrieved 2026-09-27)
- [EF Core 10.0.12 public typed property-values API](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/ChangeTracking/PropertyValues.cs) (primary source; retrieved 2026-09-27)
- [EF Core 10.0.12 values-wrapper complex-list allocations](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/ChangeTracking/PropertyValues.cs#L32-L65) (primary source; retrieved 2026-09-27)
- [EF Core 10.0.12 current-values wrapper creation](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/ChangeTracking/EntityEntry.cs) (primary source; retrieved 2026-09-27)
- [EF Core 10.0.12 live typed current-values implementation](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/ChangeTracking/Internal/CurrentPropertyValues.cs) (primary source; retrieved 2026-09-27)
- [EF Core 10.0.12 public update-entry extension contract and typed getter](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/Update/IUpdateEntry.cs#L6-L125) (primary source; retrieved 2026-09-27)
- [EF Core 10.0.12 public scoped update-adapter factory](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/Update/IUpdateAdapterFactory.cs#L19-L48) (primary source; retrieved 2026-09-27)
- [EF Core 10.0.12 public tracked entries and key-array lookup](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/Update/IUpdateAdapter.cs#L63-L74) (primary source; retrieved 2026-09-27)
- [EF Core 10.0.12 same-context and standalone tracker acquisition](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/Update/Internal/UpdateAdapterFactory.cs#L34-L49) (primary source; retrieved 2026-09-27)
- [EF Core 10.0.12 adapter forwarding of current tracked entries](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/Update/Internal/UpdateAdapter.cs#L99-L100) (primary source; retrieved 2026-09-27)
- [EF Core 10.0.12 EF.Property binding by entity and mapped property name](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Query/RelationalSqlTranslatingExpressionVisitor.cs#L688-L694) (primary source; retrieved 2026-09-27)
- [EF Core 10.0.12 mapped IN element type inference](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Query/SqlExpressionFactory.cs) (primary source; retrieved 2026-09-27)
- [EF Core 10.0.12 collection padding](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Query/SqlNullabilityProcessor.cs) (primary source; retrieved 2026-09-27)
- [EF Core 10.0.12 SQL Server collection parameter threshold](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.SqlServer/Query/Internal/SqlServerSqlNullabilityProcessor.cs) (primary source; retrieved 2026-09-27)
- [EF Core native collation equality](https://learn.microsoft.com/en-us/ef/core/miscellaneous/collations-and-case-sensitivity) (primary source; retrieved 2026-09-26)
- [EF Core 10 store-specific column collation API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.relationalpropertyextensions.getcollation?view=efcore-10.0) (primary source; retrieved 2026-09-27)
- [EF Core 10.0.12 shared-column collation fallback and runtime metadata boundary](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Extensions/RelationalPropertyExtensions.cs) (primary source; retrieved 2026-09-27)
- [EF Core 10.0.12 original table owners, inheritance, and split fragments](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Metadata/Internal/RelationalModel.cs) (primary source; retrieved 2026-09-27)
- [EF Core 10.0.12 inheritance-root key ownership and inherited lookup](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/Metadata/Internal/EntityType.cs) (primary source; retrieved 2026-09-27)
- [Doka 10.4.4 canonical physical-table collation forwarding](https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/d57f841615fc82ff332495bd474fa47f38f9ad52/src/Doka.EntityFrameworkCore.MySql/Internal/Metadata/Annotations/MySqlRelationalAnnotationProvider.cs) (primary source; retrieved 2026-09-27)
- [EF Core compiled-model generation and regeneration](https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics#compiled-models) (primary source; retrieved 2026-09-27)
- [Doka 10.4.4 JSON-table Guid decoding](https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/d57f841615fc82ff332495bd474fa47f38f9ad52/src/Doka.EntityFrameworkCore.MySql/Internal/Query/Translation/MySqlJsonTableValueEncoding.cs) (primary source; retrieved 2026-09-26)
- [EF Core composable extension plugins](https://learn.microsoft.com/en-us/ef/core/miscellaneous/plugins) (primary source; retrieved 2026-09-27)
- [EF Core 10.0.12 method translator plugin lifetime and registrations](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Query/IMethodCallTranslatorPlugin.cs) (primary source; retrieved 2026-09-27)
- [MySQL 8.4 native string comparison and STRCMP](https://dev.mysql.com/doc/refman/8.4/en/string-comparison-functions.html#function_strcmp) (primary source; retrieved 2026-09-27)
- [MySQL 8.4.11 string equality and STRCMP implementation](https://github.com/mysql/mysql-server/blob/mysql-8.4.11/sql/item_cmpfunc.cc) (primary source; retrieved 2026-09-27)
- [MySQL 8.4.11 collation callback used by STRCMP](https://github.com/mysql/mysql-server/blob/mysql-8.4.11/sql-common/sql_string.cc) (primary source; retrieved 2026-09-27)

# Configuration

This guide defines the EF Core model and context contract. See
[Hierarchy model](hierarchy-model.md) for the meaning of each stored value and
[Transactions and locking](transactions-and-locking.md) for runtime ownership.

## Register infrastructure

Install the selected NestedSet adapter and call `UseNestedSets()` on the
native provider builder inside its callback:

```csharp
var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 8, 0));

services.AddDbContext<ApplicationDbContext>(options => options
    .UseMySql(connectionString, serverVersion, mysql => mysql.UseNestedSets()));
```

The adapter adds an immutable `IDbContextOptionsExtension` selecting its exact
provider implementation. It installs shared model-finalizing conventions,
typed tree registries, the save guard, persistence boundary, and diagnostics,
plus the selected adapter's native services. It is idempotent and safe for context pooling. The
persistence boundary replaces the provider's standard relational `IDatabase`
service in either extension order; a provider-specific `IDatabase`
implementation is rejected instead of being replaced.

Registration also validates insertion identity, mapped setters, relationship
recovery and native notification-handler signatures against the loaded EF
assembly once. An incompatible framework
fails with `InvalidOperationException` naming its version and the missing
member before a context performs hierarchy writes. This checks the framework
shape; it does not replace provider tests or qualify every future EF patch.
Requalify insertion, callback rejection, rollback, and mapped refresh behavior
when upgrading EF.

Do not add any of these former registrations:

- `ModelConfigurationBuilder.UseNestedSets()`;
- `ModelBuilder.UseNestedSets()`;
- a context-constructor configuration hook; or
- a separate NestedSet service or factory registration.

## Required structural roles

Each hierarchy maps one property for every required role:

| Role | Shape | Meaning |
| --- | --- | --- |
| NodeKey | Non-null scalar primary or alternate key | Stable node identity |
| TreeId | Non-null scalar | Stable tree identity |
| Scope | Optional non-null scalar | Application partition |
| Parent | Nullable NodeKey | Direct parent; null for the root |
| Left | `long` | Inclusive left boundary |
| Right | `long` | Inclusive right boundary |
| Depth | `int` | Ancestor count; root is zero |
| Position | `long` | Dense zero-based sibling position |

Scope, TreeId, and NodeKey can use relationally mapped value converters. Their
store type, length, fixed/unicode facets, precision, scale, and collation are
part of database identity.

Registry requests validate Scope and TreeId lengths after the finalized value
converter has produced their stored representation. Byte arrays use byte
lengths. Known Doka `char`/`varchar` and Npgsql bounded character mappings
(including `bpchar`) count Unicode scalar values: one valid surrogate pair
counts once, while a combining mark counts separately. In these bounded
character checks, malformed surrogate sequences are rejected before a registry
command; values are never normalized,
replaced, or truncated by this check.

SQL Server and SQLite retain their existing UTF-16 guard. SQL Server
`nchar`/`nvarchar` capacities use UTF-16 byte-pairs, whereas ANSI capacities
depend on encoded bytes; the guard is not a substitute for that encoding
constraint. SQLite does not enforce a declared character length, but the
configured library guard remains in effect. Unknown or custom mapping classes
retain the conservative UTF-16 check rather than inferring character semantics
from a `varchar` name alone. Actual compiled models retain the same concrete
mapping classification without an additional annotation or registration.

The physical charset and collation still determine representability and
equality. For MySQL/MariaDB supplementary characters, configure a compatible
charset such as `utf8mb4`. This length correction does not change key comparers,
padding semantics, required identity roles, or the full-index byte budget. See
the [Unicode qualification](qualification/unicode-identities.md)
for native boundaries and the primary sources defining these units.

When a converted Scope, TreeId, or NodeKey has broader CLR equality than its
stored identity, configure its EF value comparer to match the stored key.
Configure the Parent comparer consistently with NodeKey. Scope and NodeKey may
participate in the entity key, and TreeId is part of the library's registry
key. NestedSet checks structural identities by converted provider values, but
cannot override EF's tracking semantics. See EF Core's
[key-comparer guidance][key-comparers].

The core interfaces provide conventional names:

- `INestedSetNode<TNodeKey, TTreeId>` for an unscoped entity;
- `IScopedNestedSetNode<TNodeKey, TTreeId, TScope>` for a scoped entity.

Interfaces are optional. Existing entities can map every role explicitly. This
illustrative `ExistingNode` has a surrogate primary key and a separate
partition-local structural key:

```csharp
internal sealed class ExistingNodeConfiguration : IEntityTypeConfiguration<ExistingNode>
{
    public void Configure(EntityTypeBuilder<ExistingNode> builder)
    {
        builder.HasKey(node => node.RecordId);
        builder.HasAlternateKey(node => new { node.PartitionId, node.NodeId });

        builder.HasNestedSet(nestedSet => nestedSet
            .HasNodeKey(node => node.NodeId)
            .HasScope(node => node.PartitionId)
            .HasTreeId(node => node.TreeId)
            .HasParent(node => node.ParentNodeId)
            .HasBounds(node => node.Start, node => node.End)
            .HasDepth(node => node.Level)
            .HasPosition(node => node.SiblingPosition));
    }
}
```

This works entirely inside `IEntityTypeConfiguration<TEntity>`. A direct
`modelBuilder.Entity<TEntity>()` call in the context is not required.

Lambda selectors are the preferred refactoring-safe form. String overloads
such as `HasTreeId("TreeId")`, `HasParent("ParentId")`, and
`HasBounds("Left", "Right")` support already mapped field-only, indexer, and
shadow properties.

## Optional Scope

`.HasScope(...)` may be omitted completely. The resulting facade is used as:

```csharp
var hierarchy = context.NestedSet<Folder>();
```

For a scoped hierarchy, bind the exact Scope before every query or operation:

```csharp
var hierarchy = context.NestedSet<Folder>()
    .ForScope(tenantId);
```

An unbound scoped facade and `ForScope(...)` on an unscoped mapping fail before
query execution. NestedSet does not infer Scope from a global query filter.

## Optional registry table name

`HasTreeRegistryTable("Reporting_HierarchyTrees")` names this hierarchy's typed
tree registry instead of the generated `DokaNestedSetTrees_<hash>`. It changes
only the physical table name, stays in the hierarchy's schema and is validated
per provider. On an existing database it is a schema change that needs an ordinary
migration. See [explicit registry table name](migrations.md#explicit-registry-table-name).

## Optional ancestor index

Selected ancestor queries can opt into an additional generated property and
index. This is a schema choice, separate from the required hierarchy roles:

```csharp
builder.HasNestedSet(nestedSet => nestedSet
    .HasTreeId(folder => folder.TreeId)
    .HasScope(folder => folder.TenantId)
    .HasParent(folder => folder.ParentId)
    .HasAncestorIndex());
```

`HasAncestorIndex(string nonLeafPropertyName = "NestedSetIsNonLeaf")` creates a
required Boolean shadow property during model finalization when the named
property does not exist. The configuration callback records the option rather
than immediately creating that property. If application configuration refers
to the property earlier, for example in a custom index, declare it explicitly
with `builder.Property<bool>("NestedSetIsNonLeaf")` first. Its database-generated
value expresses `Right > Left + 1` with a scalar CASE guarding
`Left == long.MaxValue`, where adding one would overflow and no signed Right
can satisfy the nonleaf predicate. The expression uses final mapped boundary
columns; see the [exact generated SQL](migrations.md#optional-ancestor-index).
A compatible existing Boolean property may be used instead. It cannot
also be a structural role, order criterion, key, foreign key, converted value,
or concurrency token. Conflicting defaults, generation, and save behavior fail
model finalization rather than silently changing application metadata.

The additional ascending index is:

```text
(Scope?, NestedSetIsNonLeaf, TreeId, Right, Left, NodeKey)
```

The optional Scope is omitted for an unscoped hierarchy; a repeated mapped
property appears only once. Existing bounds, sibling-position, and configured
ordering indexes remain. The flag and all index columns must share the physical
structural table. Inherited and split mappings are supported only when this
co-location can be established from their completed EF mapping.

### Current query capabilities

This table is the canonical current capability matrix. Dated qualifications
and ADR history describe their own frozen source and provider builds.
The MySql adapter directly calls the required public
`DatabaseFacade.IsMariaDb()` API to identify the configured Doka engine without
database I/O. It selects MySQL/MariaDB-specific physical strategies; it does
not activate the ancestor index. Its declared Doka minimum is 10.6.4, which
supplies engine identification, source binding, and ordered joins. Older
missing-feature discovery and legacy API-availability paths are not part of
this adapter contract. Historical package comparisons remain in the
[dated qualification](qualification/provider-migrations.md#doka-mapping-compatibility).
The generated property and index are model features on every supported
provider. Both indexed readers require the explicit model option, Doka's
independent-owner capability, its verified public `WithMaterializedSource` API,
and an eligible selection. `SelectAncestorsOrSelf` additionally requires the
verified public `StraightJoin` API.

| Selected NestedSet adapter | Strict ancestors without `HasAncestorIndex()` | Strict ancestors with `HasAncestorIndex()` | Ancestors or self |
| --- | --- | --- | --- |
| `.MySql` with its required Doka version | Source-bounded ordinary path for eligible selections | Indexed strict path when its prerequisites hold | Existing strategy without the schema option; indexed inclusive path with two directed join edges when its prerequisites hold |
| `.Postgres`, `.Sqlite`, `.SqlServer` | Existing strategy | Existing strategy; generated schema only | Existing strategy; generated schema only |

The source-bounded ordinary strict path reuses the existing public source binder,
ordered-join API, and structural indexes; it adds no generated property, index,
migration, or public option. It completes selected NodeKey, TreeId, Left, Right,
and optional Scope coordinates once, derives source envelopes per tree and Scope, and checks
visible nonleaf candidates against those necessary ranges. All four original
containment comparisons and final mapped-alias checks remain required. Global
model filters stay on the owner root; only library Scope/Tree restrictions come
from the completed source. Only a provider positively identified as MySQL by
the existing public engine API receives an additional nested binding over
matched source/owner keys. It preserves their occurrences before mapped payload
hydration and separates repeated direction changes from one merged
payload/EXISTS join search. MariaDB keeps source binding without that additional
matched-key boundary. On both routes,
source hydration is ordinary and only final owner hydration receives a directed
edge. Source completion is once per binding,
not a promise of one source evaluation across all independent composition calls.
A selection that needs hierarchy decorrelation retains the existing
independent-coordinate strategy. Other adapters retain their own portable
strategies; the MySql adapter's public Doka calls are compile-time dependencies.
Broad source envelopes can still cover a large owner population; the generated
nonleaf index remains a separate read/write tradeoff. The generated-index and
inclusive strategies keep their own qualification. MySQL's additional matched-pair
completion may limit downstream predicate pushdown and adds a physical
intermediate; its cost requires qualification for the actual workload. The same
extra boundary was rejected for MariaDB after a branched-chain sample exhausted
its recorded container memory. No additional provider API is required.

Plain scalar selections remain eligible. Query-bearing selections also qualify
when inspection proves non-hierarchy EF roots from the compiling context's
actual model, including inherited and named query filters. Ordinary
`node => facts.Any(...)`, `node => context.Facts.Any(...)` and
`node => context.Set<Fact>().Any(...)` can reach this path; hand-built expression
roots are not required.

The provider-callback `UseNestedSets()` registration installs a stateless
public EF query-expression interceptor. Source-sensitive ancestor composition
on every options-registered provider can emit an exact private marker without reading captured fields or application
getters. This normalization boundary is separate from Doka-only physical read
strategies.
At a terminal operation, EF performs its normal capture/parameter processing;
when compilation is needed, the interceptor expands the marker before relational
translation and inspects those actual normalized roots. EF still owns capture
values, cache selection and execution. Rebinding a captured query affects its
next terminal operation rather than freezing its construction-time source.

Hierarchy roots, EF reference/collection or skip-navigation expansion, raw-SQL
roots, opaque/query-valued parameters and unknown operators retain the existing
strategy. An EF-normalized root is not permission to ignore its mapping or
filters. NestedSet never evaluates an application getter to guess eligibility;
EF can evaluate ordinary captures at terminal execution under its own rules.
Model-only registration installs no interceptor and uses its non-deferred
construction path without emitting unexpandable markers. Without
`HasAncestorIndex()` the generated-index reader is unavailable; eligible Doka
strict queries can still use the source-bounded ordinary path above. Other
combinations keep their corresponding existing strategy. Normalization itself
is provider-independent; these bounded and indexed readers require the Doka
capabilities shown in the table.

`WherePathEdges`, `WhereAnyAncestorOrSelf` and `WhereNearestAncestorOrSelf`
defer their proof construction only when their input already contains an exact
owned marker. Their existing source, scope, null and endpoint guards still run
immediately. After EF normalization they reuse the same path/existence/nearest
cores; ordinary unmarked filters retain their existing construction path.
Compilation-local sharing is limited to admitted equivalent owned visible and
unbound roots. Exact metadata, scalar parameter identities and allowed literals
remain part of that comparison; arbitrary application query trees are not
interned, evaluated or retained globally.

Independently inspected fact selections retain a narrow structural query block
before source completion. Its complete unique node identity preserves source
rows and later fact/consumer occurrences; it is not a `Distinct` over report
results. Plain scalar selections retain their existing shape. See
[implementation design](implementation-design.md#optional-ancestor-index-and-statement-local-source-binding)
for the MariaDB merge boundary and its qualification requirements.

The inclusive path uses the same public ordered-join API twice: source modes
precede completed narrow candidates, and the candidate-bearing selected relation
precedes final mapped-owner hydration. Source-entity hydration and other
consumer/payload-source joins stay ordinary joins. The two local edges do not
order the entire SELECT, add a result ordering or introduce another capability.
Missing ordered-join capability does not disable the strict indexed path. PostgreSQL,
SQLite, SQL Server, and ineligible selections retain the existing read strategy
and its shared overflow-safe nonleaf predicate. Configuring the index alone
does not select a different query or establish a performance benefit. The
required Doka APIs are published in 10.6.1. Their
[historical index qualification](qualification/query-composition.md#optional-ancestor-access)
records read/write costs. The
[2026-10-20 correction qualification](qualification/query-composition.md#fact-selections-and-consumer-filters)
adds fact-source and downstream-consumer evidence, including higher allocation
costs and case-matched timing limits. Select the option for the actual workload.
The [provider qualification](qualification/provider-migrations.md#doka-mapping-compatibility)
distinguishes the published Doka 10.6.1 entity-split GUID failure from the
corrected local source. That dated local graph passed the recorded ordinary
MySQL/MariaDB regressions, including the formerly failing split mappings.
Repository qualification now declares published Doka 10.6.4 and SafeMigrations
10.7.0; the optional MySQL adapter requires Doka [10.6.4,10.7.0). Only the new
adapter contract is retained in tests. The earlier pending pin update is
complete. The earlier Doka 10.6.2 [2026-10-25 public-binary run](qualification/provider-migrations.md#published-runtime-qualification-2026-10-25)
passed all 4,289 MySQL/MariaDB and 2,649 Unit cases, with ordinary and optional
migration scope recorded separately. Local ARM exclusions remain native SQL
Server work for x64 CI; neither private packages nor these exclusions count as
a native SQL Server pass.

A configured ancestor index read on an adapter without an optimized reader
emits `Doka.EntityFrameworkCore.NestedSet.Query.AncestorIndexWithoutReader`
(70004), at Information level and at most once per actual `DbContext`. Observe
it through EF logging, `LogTo`, or `DiagnosticSource`; configured warning levels
and filters still apply. The generated column/index remains valid while the
query uses that adapter's portable reader. This diagnostic neither rejects the
model nor changes query semantics, and emits no application identity, SQL, or
request values.

The former missing-engine/source-binding/ordered-join diagnostics are removed:
the MySql adapter's declared Doka minimum provides all three public APIs and
the compiler binds them directly. Missing or wrong adapter registration is an
explicit configuration error rather than an optional feature notification.

Historical published Doka 10.5.0 also had a separately reproduced implicit-string
index-budgeting defect for this schema. The corrected private provider passed
the actual implicit-length model on both engines; those recorded results are
not relabeled as qualification of the current published 10.6.4 pin. Neither provider
version selection nor an index option authorizes arbitrary identity truncation; see
[physical mapping and provider budgeting](implementation-design.md#optional-ancestor-index-and-statement-local-source-binding)
and the [combined-provider acceptance](qualification/query-composition.md#optional-ancestor-access)
for the exact boundary.

### Choose for the workload

| Workload | Decision and evidence to require |
| --- | --- |
| Selective upward reads over large trees with the required Doka features | Candidate for opt-in. Compare the actual selection, complete results, native work, and construction allocation; the index does not make every range selective. |
| Fact-selected sources or downstream `Any`/`Contains`/negated-existence filters | Qualify application-owned fact indexes for the actual equality/filter/join columns. A bounded hierarchy plan does not bound scans of unrelated application rows. |
| Broad selections or long selected chains | Measure before opting in. Candidate matching and temporary work can dominate even when hydration is bounded. |
| Frequent imports, moves, or repair | Include write amplification and index storage. Every coordinate update maintains the extra access path. |
| A provider or source shape using the existing reader | Keep the default unless the application has an independently measured use for the generated column/index. The library provides no optimized reader for a schema-only path. |
| Portable EF model shared across providers | The option remains valid on every supported provider. Review migrations and qualify each database separately; portability does not imply equivalent plans. |

The [2026-10-19 frozen qualification](qualification/query-composition.md#optional-ancestor-access)
measured 23%/36% more index pages and 6%/121% longer imports on MariaDB/MySQL
respectively in that fixture. Its indexed read medians were lower in only
24/42 and 21/42 comparable cases; this is not a statistical significance test.
Construction and read allocations also rose in many cases. These are observed
costs and opportunities, not universal
budgets or a promise that opting in is always beneficial. The
[performance guide](performance.md#optional-ancestor-index-and-shared-source-boundaries)
keeps those measurements separate from later source qualification.

The flag is generated on insert and structural updates. The library refreshes
its tracked value through the normal bounded structural refresh; applications
must not supply or modify it as hierarchy structure. Read it, when needed, with
`EF.Property<bool>(folder, "NestedSetIsNonLeaf")` inside an EF query. The option
does not add a required property to an entity's public CLR contract.

For an EF property-bag entity, the generated value is a required Boolean
indexer property instead of a shadow property. Leave the generated dictionary
entry uninitialized: EF's natural missing-key sentinel supplies its default
until the database generates the value. An explicit non-null indexer sentinel
is incompatible and rejected; applications do not need a workaround that adds
the flag to each dictionary before insertion.

Scaffold and apply a migration before using the changed model, and regenerate
application-owned compiled models. The extra index consumes storage and adds
maintenance to coordinate writes, so it is deliberately opt-in. It exposes an
access path, not a guarantee that the optimizer will select it or avoid every
population scan. See the [migration contract](migrations.md#optional-ancestor-index)
and [query strategy](query-composition.md#optional-selected-ancestor-access-path).

Compiled models preserve the schema/mapping descriptor; the current adapter is
selected separately through its native options callback. Regenerate the model
after configuration changes, then qualify it against that actual adapter and
provider. The [generated-model regression](query-composition-regression-coverage.md#optional-ancestor-index-and-source-binding)
retains query and value-lifecycle checks for the selected adapter. Earlier
optional-feature build cohorts remain dated historical evidence; metadata
success alone does not prove current query eligibility or native x64 SQL Server
execution.

## Parent relationship

The final convention creates or validates a restrictive self-FK when the EF
model shape can represent it. For an unscoped hierarchy, its dependent and
principal properties are:

```text
Parent -> NodeKey
```

For a scoped hierarchy, they are:

```text
(Scope, Parent) -> (Scope, NodeKey)
```

TreeId is deliberately absent. A subtree may move between trees inside the
same Scope, so including TreeId would require a self-referential key update.

When the domain wants navigations, configure the exact same relationship:

```csharp
builder.HasOne<Folder>()
    .WithMany()
    .HasForeignKey(folder => new { folder.TenantId, folder.ParentId })
    .HasPrincipalKey(folder => new { folder.TenantId, folder.Id })
    .OnDelete(DeleteBehavior.Restrict);
```

Use `Restrict` or `NoAction`. Cascade behavior can delete children before the
hierarchy algorithm reparents or deletes the intended branch. An incompatible
or competing relationship on the Parent property is rejected while the model
is finalized.

## Sibling ordering

Ordering is optional. Without an order expression, explicit placement controls
`Position`. With a configured expression, insert, move, tracked changes, bulk,
and rebuild use the same database ordering:

```csharp
builder.HasNestedSet(nestedSet => nestedSet
    .HasTreeId(folder => folder.TreeId)
    .HasScope(folder => folder.TenantId)
    .HasParent(folder => folder.ParentId)
    .OrderBy(folder => folder.Name)
    .ThenBy(folder => folder.Type)
    .HasOrderMode(NestedSetOrderMode.Strict));
```

The NodeKey is appended as the final ascending tie breaker when absent.
Nullable criteria require explicit `NullSortOrder.First` or
`NullSortOrder.Last`. Store collation and value conversion define comparison
semantics. See [Sibling ordering](ordering.md).

## SaveChanges integration

Explicit hierarchy operations execute immediately and need no context base
class. A tracked Parent or configured-order change needs an async transaction
around the ordinary EF save and the structural update.

For a new context, derive from `NestedSetDbContext`:

```csharp
public sealed class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options)
    : NestedSetDbContext(options)
{
}
```

For an application context with another base class or existing save rules,
compose the public extensions:

```csharp
public override Task<int> SaveChangesAsync(
    bool acceptAllChangesOnSuccess,
    CancellationToken cancellationToken = default)
{
    ApplyApplicationSaveRules();

    return this.SaveNestedSetChangesAsync(
        acceptAllChangesOnSuccess,
        token => base.SaveChangesAsync(false, token),
        cancellationToken);
}

public override int SaveChanges(bool acceptAllChangesOnSuccess)
{
    ApplyApplicationSaveRules();

    // ReSharper disable once MethodHasAsyncOverload
    // WHY: This override implements EF Core's explicitly synchronous contract.
    return this.SaveNestedSetChanges(
        acceptAllChangesOnSuccess,
        () => base.SaveChanges(false));
}
```

The async base delegate must use `acceptAllChangesOnSuccess: false`. The
coordinator calls it exactly once and accepts changes only after payload and
structure both succeed. Existing interceptors and application callbacks remain
active. They must not recursively save, detach planned entries, accept changes,
or mutate additional hierarchy structure or configured ordering properties during
the callback. A `SavingChanges` callback may add an ordinary audit or outbox entity.
For an exact-TreeId insertion, it may also change payload on an already-tracked
node in an unaffected tree. EF includes these writes in the same save and
transaction as the managed hierarchy insertion.

A library-owned insertion validates its write plan once, at EF's persistence
step after every `SavingChanges` callback and before the first command.
`SavedChanges` callbacks and code after the base save may inspect the tracker
freely. If a later insertion step fails and the transaction rolls back, ordinary
callback writes that EF already accepted return to their pending `Added`,
`Modified`, or `Deleted` state, including temporary generated keys, so a later
save can persist them. A callback that suppresses the save or removes any
planned entry fails the insertion.

The synchronous path supports payload-only saves and rejects hierarchy work
that requires asynchronous coordination. Without the wrapper, the registered
guard rejects direct structural, Parent, and configured-order writes before
SQL.

### Manual change detection

Ordinary EF saves, the NestedSet save wrappers, and single/bulk/forest facade
insertions preserve `ChangeTracker.AutoDetectChangesEnabled`. With `false`,
an unmarked CLR payload edit on an existing snapshot-tracked entry is not
discovered for persistence. Explicitly marked properties, Added/Deleted
application entries, notifications, and application calls to `DetectChanges()`
retain EF's normal behavior. This applies to unrelated entities and to
nonstructural payload on hierarchy nodes. It does not freeze payload values:
Added entries and already marked properties still write their current values.

The structure guards still inspect hierarchy roles, configured order criteria,
and relevant navigation membership, including unmarked CLR changes. They do
not run global or per-entry detection, mark properties, or fix up unrelated
tracked principals. The existing relational persistence hook checks again
after save callbacks and cascade processing, before commands execute; it never
changes EF's prepared write set.

Collection removals count only while the removed dependent is still tracked.
EF can retain detached instances in its collection snapshot, so those stale
references do not block subsequent saves, even after explicit detection.
New untracked hierarchy members remain guarded because EF may attach them.
Replacing a principal instance with the same relevant FK values is not itself
a hierarchy change. An unknown untracked shadow key or generated-key sentinel
cannot prove that equality. Normal EF identity-conflict rules still apply.
For composite relationships, only actual effects on hierarchy roles, configured
ordering, or dependent deletion request coordination. EF may exclude an
overlapping Scope FK member when clearing an optional ordinary owner reference;
changing only its ordinary owner-id member remains payload work. Clearing the
tracked dependent's own Parent reference can still sever ParentId after the
old parent was detached and therefore remains guarded.
Required cascading owner relationships are lifecycle-relevant even when their
FK fields are ordinary payload: severing them or stealing a unique owner from
another tracked hierarchy node can delete a hierarchy node. Those effects
remain guarded, while assigning an unused owner without hierarchy effects
remains ordinary payload work.

Deleting an ordinary principal must not bypass facade deletion through client
cascade processing. If EF marks tracked hierarchy nodes Deleted, the final
persistence guard rejects the save before SQL. Perform the hierarchy deletion
explicitly through the facade first. This guard is not a database-trigger or
untracked database-cascade inspector; the complete EF mapping and existing
database integrity requirements still apply.

With detection disabled, explicitly record Parent or configured-order changes
before calling `SaveNestedSetChangesAsync`. For example:

```csharp
context.ChangeTracker.AutoDetectChangesEnabled = false;
folder.ParentId = destinationId;
context.Entry(folder).Property(node => node.ParentId).IsModified = true;

await context.SaveChangesAsync(cancellationToken);
```

EF entry `CurrentValue` assignment or notification tracking can also record
the change. A raw unmarked Parent/order edit is rejected, rather than ignored
or discovered through a global detection pass. Facade methods already supply
their explicit insertion/mutation plan. Their preflight still rejects existing
registered pending writes outside a coordinated save.

The coordinated save captures every hierarchy role and ordering value of its
original candidates before temporarily suppressing Parent writes. Navigation
effects compare with the authorized destination, while scalar guards separately
retain the suppressed physical Parent value. Applications may maintain the
matching Parent reference and inverse collection when explicitly marking the
Parent change; these matching edits do not introduce another operation. Callbacks
cannot change that plan, introduce unmarked structural work, or sever relevant
relationships. Explicit ordinary callback writes can persist atomically;
unmarked snapshot-tracked callback payload remains unsaved. The same guards
run for managed insertions, including planned-node navigations and tracked
nodes absent from EF's pending write list. No guard turns automatic detection
on or marks application properties. An application that explicitly enables
detection in a callback or calls `DetectChanges()` requests native EF detection;
NestedSet does not undo that request. Keep contexts short-lived to bound the
existing transaction rollback snapshot.

Rollback preserves pending CLR references, collection instances and ordered list
members independently from EF's installed FK and navigation snapshots. It does
not normalize a pending graph into the graph that change detection would produce.
Collection snapshot membership is restored; the private representation of an
absent sidecar versus an empty snapshot is not part of this guarantee.
Pre-save endpoint evidence includes initially unchanged FKs. Their supplemental
recovery restores dependents whose native identity changed or whose explicitly
marked FK write ends at the captured identity, while unrelated raw
callback additions/removals remain pending. Existing changed/lifecycle captures
keep their complete pre-save graph authority. If a supplemental collection was
null but unrelated pending raw members now need a container, that new container
is retained; without those members, null is restored. This does not change the
null-storage guarantee of an existing authoritative capture.
If raw neighbor reordering conflicts with original interleaving, selected original
occurrences and unselected pending members each retain their relative order.
Clearing a round-trip FK's write flag leaves its CLR reorder pending.
That pending order does not authorize loss of the original native collection
member. Recovery checks native membership again after undoing library-owned
additions and repairs captured dependents without consuming the independent
unmarked CLR reorder. Members outside the captured dependent set remain pending.
Required-FK recovery also preserves conceptual-null intent and native dependent
buckets independently of equal scalar snapshots, including nulls already pending
before save. This does not discover unrelated unmarked payload.
If a native index was initialized from a pending raw FK, its current-value bucket
is preserved separately from the installed relationship-snapshot bucket. Both
remain independent of CLR references and inverse collection membership.

EF can promote an Unchanged OwnsOne or ordinary table-sharing peer to Modified
without setting any property flags during command preparation. Coordinated
rollback restores its captured state before replaying current/original values,
generated or temporary storage and pending CLR values. This preserves the
original save exception and allows the existing retry contract; it does not
normalize peers that were already Modified, Added or Deleted. The recovery uses
an unmodified property flag, not the whole-entity Unchanged setter that would
reject raw complex graphs. A separate complex journal restores captured CLR
containers parent-first before ordinal or scalar access. It preserves null versus
empty, list order, duplicate references and the original list, element and
optional-reference instances when a callback shrinks or replaces the graph.
Current and original native element slots remain independent: pending Deleted
originals and unregistered Detached raw additions do not become a new baseline.
Native children hidden by a previously raw-null or shortened parent retain their
independent scalar originals, flags and available old CLR instances too.
Accepted Added/Deleted owners recover their retained complex authorities before
EF cascades their lifecycle restoration. Scalar values, originals and flags
settle afterward; struct references receive their mapped CLR copyback as well.
This does not call DetectChanges or weaken the existing hierarchy guards.

A callback can compose several collection changes. Recovery preserves captured
native current/original lane lengths and holes after the temporary graph is
removed, including pending Added elements with nested lists. It does not shrink
those lanes merely to the caller's raw current CLR count. Unchanged containers
do not require indexed writes or mapped setter calls, so supported read-only
collections and copying setters keep their no-op behavior. Struct-reference
primitive collections retain their comparer-snapshotted scalar values; replaying
a captured struct box must not overwrite that restoration.

Retirement and reinstallation of native entries use separate temporary access
graphs, because a callback can shift the ordinals retirement has to walk. This
covers pending Deleted elements and their descendants, and pending Added children
of a removed owner. Field-mapped collections, the default, are staged without
touching the caller's list: no events are raised and validating `Add` code does
not run, because temporary slots can be null. A collection mapped with
`PropertyAccessMode.Property` is staged in place, since its setter could copy; its
events can observe temporary membership during rollback, and a custom type that
rejects null then receives an existing element in those slots, or none if the list
holds no element at all. Struct references
are always replayed rather than compared through caller `Equals`, and nullable
struct references restore mutable primitive collections too.

A read-only view whose hidden backing list a callback changed cannot be written
through, and a copying setter cannot keep an assigned instance. Recovery restores
their exact membership in a mapped copy or in the setter's copy, so only those two
containers can change identity. Scalars of raw CLR elements that EF never registered
are journaled and restored too. After a callback's `ChangeTracker.Clear()`, every
captured entry is tracked again with its state, values and baselines.

The operation's snapshot is the ownership boundary for complex entry handles.
Handles belonging to its captured complex preimage retain their exact native
identity, state and ordinals. Native occurrences first created after capture
are discarded from the restored caches, including nested callback additions
whose CLR parent is already gone. Those post-capture handles are invalid after
rollback; reacquire them from the restored graph rather than expecting their
old State to become Detached. Callback-created entries already included in a
managed save's later persistence-boundary snapshot remain captured authority.

Rollback may temporarily supply an access graph from captured, original and live
instances while EF restores native registrations and hidden scalar intent.
That graph is cleanup storage, not a post-callback rollback baseline. Its
failure-only receipts restore old original/live containers before final null
replay hides their paths; the captured CLR preimage is then reinstated.
Hidden struct references are read again through their retained original element
after mapped copyback, rather than through an obsolete captured struct box.
Fixed-size runtime collections are restored by slot under a supported EF
collection mapping. Struct complex references are supported; EF's rejection of
struct collection elements remains unchanged. The internal adapter validates
cached native-slot reads, mapped setters/factories and EF state/ordinal methods.
Scalar and complex-collection originals share the validated rollback-only
original-buffer writer described in D-004.
The native adapter bundle initializes when complex capture first needs its
readers. An incompatible signature in that bundle rejects capture before the
first persistence command. A distinct failure of the rollback-only original writer after definite
database rollback retains the recovery-failure/discard-context boundary.
Managed inserts also capture the exact unchanged sharing peers of callback
writes before provider preparation, without capturing unrelated tracked rows.
The [implementation guide](implementation-design.md#ordering-and-coordinated-saves)
explains native ownership and the validated framework seam.
[Recovery controls](regression-coverage.md#manual-detection-and-hierarchy-guards)
cover composed callbacks, exact lanes, sharing peers and retry; their source
inventory is not evidence that every permitted EF patch or native engine ran.

Manual detection preserves native EF behavior. In a shared-table UPDATE, EF can
create Added JSON element entries for unmatched raw list ordinals during command
preparation, marking the JSON collection for serialization without calling
DetectChanges. Such cardinality changes can therefore persist, even if the
collection flag was false before saving. Existing unmarked scalar edits remain
unsaved in the qualified controls. NestedSet does not suppress that native EF
write or enable automatic detection; rollback still restores the exact pending
graph and original snapshots. The qualification includes a plain EF model with
no NestedSet registration to establish this distinction.

Ordinary FK updates also baseline proven saved inverse membership in prepared
collections. This applies equally to saves with or without Parent/order work and
to registered native synchronous/asynchronous saves. Raw unmarked FK values and
later command/completion mutations cannot prove an earlier write. A coordinated
save records only each native member it adds for a callback-selected owner. It
does not copy that owner's post-callback collection as a rollback baseline.
Recovery removes those additions before restoring authoritative pre-save native
snapshots, preserving other members changed through EF fixup and pending CLR
collections independently. Ordinary EF saves retain their existing transaction semantics;
an exception after their SQL committed is not a coordinated rollback.

A successful facade insertion baselines only the saved node's proven inverse
membership in an already tracked external owner. The inserted node may therefore
remain in that owner's prepared CLR collection after the node is detached, without
blocking a later save. With manual detection, that collection may contain the full
validated subtree or forest input, including future batches. This permission is
operation-local and requires exact input references, mapping and matching FK
values. Only the active batch may be saved. Unknown members, wrong relationships
and prematurely Added future inputs remain rejected. With automatic detection,
preflight still rejects prepared graphs that EF has already registered as pending
writes before the facade begins.

Generated and temporary EF values can differ from the entity's CLR member.
Candidate authorization captures both independently, so a later raw CLR key
edit cannot hide behind an unchanged EF sidecar. An unchanged, initially
captured difference is preserved; shadow properties have no CLR value to
compare. Ordinary guards also inspect CLR storage when a real sidecar could
conceal a structural change.

See EF's [change detection documentation](https://learn.microsoft.com/en-us/ef/core/change-tracking/change-detection)
for the distinction between automatic detection, explicit detection, and
changes recorded through EF entry APIs.

## Concurrency tokens

Store-generated concurrency tokens are refreshed after structural SQL. An
application-managed concurrency token is rejected unless the mapping explicitly
chooses the preservation policy:

```csharp
.PreserveApplicationConcurrencyTokens()
```

This policy means structure-only set updates do not change that token. The
application remains responsible for advancing and validating it with domain
payload changes.

Generated tokens declared only on a derived entity also participate. Owner-level
queries project each token only for the subtype that maps it; sibling rows do
not receive that property. Coordinated saves honor both tracker acceptance
modes. Single and bulk insertions return current generated CLR values even
when the facade uses the configured base type. A late failure restores captured
input values without changing generated properties of inputs not yet staged.

Single, forest, and subtree insertion guard assigned and generated NodeKeys
across save callbacks. Ordinary payload and non-hierarchy audit writes remain
permitted; key, Scope, TreeId, and other managed structure edits are rejected.
When a definite failure rolls back the database, exact introduced entries and
owned payload are released without clearing unrelated tracked entities.
Single insertion finishes detachment before commit or savepoint release, so
a rejecting state callback still rolls back the write. Captured generated CLR
leaves, owned keys, and ownership foreign keys restore to their pre-fixup values;
ordinary callback payload and non-ownership business foreign keys remain
caller-owned. The input root and its owned aggregate must be detached before
insertion; already-tracked caller graph members are rejected and preserved.
Generated sidecar values and the CLR key representation are checked separately
before acceptance can overwrite a callback edit. A cleanup failure preserves
the original error in an `AggregateException`; discard that context rather
than retrying with partially restored tracking. See
[insertion recovery](bulk-import.md#failure-and-restoration).

## Model compatibility

The qualified model shapes include:

- scalar primary NodeKeys;
- composite primary keys with a scalar alternate NodeKey;
- shadow and field-only structural properties;
- converted key, Scope, and TreeId values;
- TPH and qualified TPT mappings, including trees containing different derived
  entity types;
- table and entity splitting within the documented single-write-fragment
  contract;
- temporal current-table mutations;
- compiled models; and
- pooled contexts.

Configure an inherited hierarchy on its common mapped owner. Coordinated
`SaveChangesAsync` then treats changes to different derived types as one
hierarchy: a Parent may refer to another derived type, base-property ordering
applies across sibling types, and structural refresh includes tracked derived
descendants. The owner is the type with the NestedSet configuration, not
necessarily EF's inheritance root. Named shared mappings and independently
configured concrete TPC stores retain their separate identities.

For a NodeKey converted to string storage, parent comparisons use the
principal key's effective database collation just as native string keys do.
The query expression retains the model key type and its converter. Different
Parent and NodeKey column collations therefore cannot silently change which
parent a stored alias resolves to. This does not replace an application's
key converter or require case-insensitive keys.

Regenerate a compiled model whenever the NestedSet version or hierarchy
configuration changes. The generated metadata includes registry table names;
an old compiled model may otherwise address a table that the current migration
has renamed. EF Core documents this
[synchronization requirement][compiled-models].

Compiled-model qualification and global-query-filter qualification are separate:
EF Core does not support global query filters in compiled models. Use a normal
runtime model when the hierarchy entity has a global query filter; the
[compiled-model limitations][compiled-models] also apply to NestedSet.

[compiled-models]: https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics#compiled-models
[key-comparers]: https://learn.microsoft.com/en-us/ef/core/modeling/value-comparers#key-comparers

The model fails early for keyless nodes, TPC polymorphism, ambiguous Parent
relationships, structure spread across incompatible write fragments, and
unsupported property-role types.

## Generated schema

The final model contains:

- positive-bound, depth, and position checks where the table shape supports
  them;
- the restrictive self-FK where EF can represent it;
- indexes derived from Scope, TreeId, bounds, Parent, Position, ordering
  criteria, and NodeKey; and
- one typed registry per hierarchy with Scope when present, TreeId, Revision,
  and Lifecycle.

The registry copies store type, conversion, length, precision, scale, fixed or
unicode facets, and collation from the hierarchy identity. It is library-owned
and is included in ordinary EF migrations. Applications do not add or delete
registry rows directly.

SafeMigrations is optional. See [Migrations and indexes](migrations.md) for the
exact paths and adoption procedure, including the
[generated-column adapter contracts and historical limits](migrations.md#optional-safemigrations-generated-column-contracts).

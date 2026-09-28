# Configuration

This guide defines the EF Core model and context contract. See
[Hierarchy model](hierarchy-model.md) for the meaning of each stored value and
[Transactions and locking](transactions-and-locking.md) for runtime ownership.

## Register infrastructure

Call `UseNestedSets()` on the `DbContextOptionsBuilder` after selecting the
provider:

```csharp
var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 8, 0));

services.AddDbContext<ApplicationDbContext>(options => options
    .UseMySql(connectionString, serverVersion)
    .UseNestedSets());
```

This `IDbContextOptionsExtension` installs model-finalizing conventions, typed
tree registries, provider capabilities, the save guard, the persistence
boundary, and diagnostics. It is idempotent and safe for context pooling. The
persistence boundary replaces the provider's standard relational `IDatabase`
service in either extension order; a provider-specific `IDatabase`
implementation is rejected instead of being replaced.

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

Interfaces are optional. Existing entities can map every role explicitly:

```csharp
internal sealed class KpiConfiguration : IEntityTypeConfiguration<Kpi>
{
    public void Configure(EntityTypeBuilder<Kpi> builder)
    {
        builder.HasKey(kpi => kpi.RecordId);
        builder.HasAlternateKey(kpi => new { kpi.ProjectId, kpi.NodeId });

        builder.HasNestedSet(nestedSet => nestedSet
            .HasNodeKey(kpi => kpi.NodeId)
            .HasScope(kpi => kpi.ProjectId)
            .HasTreeId(kpi => kpi.TreeId)
            .HasParent(kpi => kpi.ParentMetricId)
            .HasBounds(kpi => kpi.Start, kpi => kpi.End)
            .HasDepth(kpi => kpi.Level)
            .HasPosition(kpi => kpi.SiblingPosition));
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

## Model compatibility

The qualified model shapes include:

- scalar primary NodeKeys;
- composite primary keys with a scalar alternate NodeKey;
- shadow and field-only structural properties;
- converted key, Scope, and TreeId values;
- TPH and qualified TPT mappings;
- table and entity splitting within the documented single-write-fragment
  contract;
- temporal current-table mutations;
- compiled models; and
- pooled contexts.

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
exact paths and adoption procedure.

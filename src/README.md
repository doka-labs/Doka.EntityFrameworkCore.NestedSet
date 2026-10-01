# Doka.EntityFrameworkCore.NestedSet

Nested-set hierarchies for Entity Framework Core, with explicit tree identity,
optional scope isolation, composable queries, and atomic structural changes.

## Packages

| Package | Purpose |
| --- | --- |
| `Doka.NestedSet` | EF-independent node contracts, validated bounds, and ancestry predicates |
| `Doka.EntityFrameworkCore.NestedSet` | EF mapping, hierarchy queries, mutations, ordering, and repair |

The EF package references the core package transitively. Neither shipping
package depends on a specific database provider or SafeMigrations.

The 10.x package line targets .NET 10 and EF Core 10. The first planned release
is `10.0.0-rc.1`; stable `10.0.0` follows a separate release preparation. The EF
package brings in the matching core package transitively:

```bash
dotnet package add Doka.EntityFrameworkCore.NestedSet --version 10.0.0-rc.1
```

The first RC is in preparation; the command is available after its NuGet
publication. Use the exact version from the matching
[GitHub release](https://github.com/doka-labs/Doka.EntityFrameworkCore.NestedSet/releases)
or NuGet package page. Add the application's EF Core provider separately.

## Identify and configure a hierarchy

Each tree has a stable `TreeId` and its own `1..2N` bounds. An optional Scope
adds a partition, such as a tenant. Equal bounds in different trees do not
identify the same node. `ParentId` and `Position` preserve adjacency and sibling
order; `Left`, `Right`, and `Depth` are derived structure.

Entities may implement the optional core interfaces or use explicit mapping.
The following unscoped entity needs no additional parent or tree entity:

```csharp
public sealed class Folder
{
    public long Id { get; private set; }
    public Guid TreeId { get; private set; }
    public long? ParentId { get; private set; }
    public string Name { get; set; } = string.Empty;
    public long Left { get; private set; }
    public long Right { get; private set; }
    public int Depth { get; private set; }
    public long Position { get; private set; }
}
```

Configure it through the application's normal EF configuration:

```csharp
builder.HasKey(folder => folder.Id);
builder.Property(folder => folder.Name)
    .HasMaxLength(256)
    .IsRequired();

builder.HasNestedSet(nestedSet => nestedSet
    .HasNodeKey(folder => folder.Id)
    .HasTreeId(folder => folder.TreeId)
    .HasParent(folder => folder.ParentId)
    .HasBounds(folder => folder.Left, folder => folder.Right)
    .HasDepth(folder => folder.Depth)
    .HasPosition(folder => folder.Position)
    .OrderBy(folder => folder.Name)
    .HasOrderMode(NestedSetOrderMode.Strict));
```

`builder` is an `EntityTypeBuilder<Folder>`, including one supplied to
`IEntityTypeConfiguration<Folder>`. `HasScope(...)` may be omitted completely.
When configured, select that scope through `ForScope(scope)` before querying
or mutating the hierarchy. Strict ordering sorts each sibling group; manual
placement requires `NestedSetOrderMode.AllowManualPlacement`.

## Register and use the EF facade

Keep the application's normal `DbContext` and `DbSet<Folder>`. Register NestedSet
once through context options. With the Doka MySQL/MariaDB provider:

```csharp
services.AddDbContext<ApplicationDbContext>(options => options
    .UseMySql(connectionString, serverVersion)
    .UseNestedSets());
```

`serverVersion` is the Doka capability profile matching the configured server.
Use the provider's normal configuration for PostgreSQL, SQL Server, or SQLite,
then call the same `UseNestedSets()` extension.

Create a root and child through the metadata-validated facade:

```csharp
var folders = context.NestedSet<Folder>();
var treeId = Guid.NewGuid();
var root = new Folder { Name = "Root" };
var documents = new Folder { Name = "Documents" };

await folders.InsertRootAsync(root, treeId, cancellationToken);
await folders.InsertChildAsync(documents, root.Id, cancellationToken);
```

Queries resolve their anchor in SQL without requiring the caller to preload it:

```csharp
var matchingNodes = await folders
    .InTree(treeId)
    .Nodes
    .Where(folder => folder.Name.StartsWith("D"))
    .ToListAsync(cancellationToken);

var ancestors = await folders
    .AncestorsOf(documents.Id)
    .Where(folder => folder.Name == "Root")
    .ToListAsync(cancellationToken);
```

Queries respect EF query filters and are no-tracking by default. Whole trees
are returned in `Left` preorder. A global business `OrderBy` would reorder
different levels together; configure sibling ordering on the model instead.

## Writes and recovery

Use explicit facade operations for insert, move, detach, delete, bulk import,
and rebuild. `DeleteAsync` promotes a non-root node's children;
`DeleteSubtreeAsync` removes the branch. Deleted tree identities remain
tombstoned until a separately authorized purge.

Normal payload saves remain EF saves. Automatic tracked Parent or sorting-field
changes require `SaveNestedSetChangesAsync` around the application's base save,
or the optional `NestedSetDbContext` base class. The registered guard rejects
uncoordinated structural changes before writing inconsistent data. A normal
`DbSet.Add` is not a substitute for a hierarchy insertion operation.

Mutations use a library-owned transaction or a savepoint in an eligible caller
transaction. They preserve caller commit ownership and restore tracked state
after rollback. Callers must retain the context's single-operation discipline
and own authorization, cancellation, and unknown-commit-outcome recovery.

## Schema and diagnostics

Normal EF migrations include the typed tree registry, structural checks, and
indexes derived from tree identity, bounds, adjacency, and configured ordering.
SafeMigrations is an optional integration, not a prerequisite.

`ValidateAsync`, `PlanRebuildAsync`, and `RebuildAsync` are available from the
selected tree. Repair reconstructs derived structure from persisted adjacency;
it does not invent missing or ambiguous parents.

`NestedSetException.Code` exposes stable failure codes. Activities and metrics
use bounded labels and exclude node keys, Scope values, TreeIds, and entity
payloads. Applications own business audit records and context lifetime.

The packages are licensed under MIT.

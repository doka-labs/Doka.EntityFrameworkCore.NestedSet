# Getting started

This guide configures one tenant-scoped folder hierarchy on the Doka
MySQL/MariaDB provider.

## Install

The application normally references the EF package and one qualified provider:

```bash
dotnet package add Doka.EntityFrameworkCore.NestedSet --version 10.0.0
dotnet package add Doka.EntityFrameworkCore.MySql --version 10.4.4
```

The first stable NestedSet package has not been published yet. Confirm the
version on NuGet.org and its matching GitHub release before using this command.

## Define the entity

The entity stores adjacency, a stable tree identity, and derived nested-set
coordinates. Private setters are supported:

```csharp
public sealed class Folder
{
    private Folder() { }

    public Folder(string name, string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        Name = name;
        Type = type;
    }

    public long Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid TreeId { get; private set; }
    public long? ParentId { get; private set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public long Left { get; private set; }
    public long Right { get; private set; }
    public int Depth { get; private set; }
    public long Position { get; private set; }

    /// <summary>Changes the parent for the next coordinated hierarchy save.</summary>
    public void ChangeParent(long? parentId) => ParentId = parentId;
}
```

The application supplies payload values. NestedSet assigns Scope, TreeId,
ParentId, bounds, depth, and position during hierarchy operations.

## Configure the entity

Configuration works inside an ordinary
`IEntityTypeConfiguration<TEntity>`:

```csharp
internal sealed class FolderConfiguration : IEntityTypeConfiguration<Folder>
{
    public void Configure(EntityTypeBuilder<Folder> builder)
    {
        builder.ToTable("Folders");
        builder.HasKey(folder => folder.Id);

        builder.Property(folder => folder.Name)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(folder => folder.Type)
            .HasMaxLength(64)
            .IsRequired();

        builder.HasNestedSet(nestedSet => nestedSet
            .HasNodeKey(folder => folder.Id)
            .HasScope(folder => folder.TenantId)
            .HasTreeId(folder => folder.TreeId)
            .HasParent(folder => folder.ParentId)
            .HasBounds(folder => folder.Left, folder => folder.Right)
            .HasDepth(folder => folder.Depth)
            .HasPosition(folder => folder.Position)
            .OrderBy(folder => folder.Name)
            .ThenBy(folder => folder.Type)
            .HasOrderMode(NestedSetOrderMode.Strict));
    }
}
```

The final convention creates or validates the parent identity, check
constraints, typed tree registry, and required indexes. A navigation is
optional. When one is configured for this scoped model, use `(TenantId,
ParentId)` as the FK and `(TenantId, Id)` as its principal key.

Omit `.HasScope(...)` completely for an unscoped hierarchy. In that model,
`TreeId` alone identifies a tree and callers do not use `ForScope(...)`.

## Configure the context

`UseNestedSets()` is the only infrastructure registration:

```csharp
var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 8, 0));

services.AddDbContext<ApplicationDbContext>(options => options
    .UseMySql(connectionString, serverVersion)
    .UseNestedSets());
```

The context remains an ordinary EF Core context. This version derives from the
optional base because it wants automatic Parent and order-property maintenance
during `SaveChangesAsync`:

```csharp
public sealed class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options)
    : NestedSetDbContext(options)
{
    public DbSet<Folder> Folders => Set<Folder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
```

An application that already owns another base class composes
`SaveNestedSetChangesAsync` in its existing override. See
[Configuration](configuration.md) for the exact pattern.

## Create and query two independent trees

Bind the optional Scope once, then supply the stable TreeId when creating each
root:

```csharp
var folders = context.NestedSet<Folder>()
    .ForScope(tenantId);

var liveTreeId = Guid.NewGuid();
var archiveTreeId = Guid.NewGuid();
var liveRoot = new Folder("Root", "system");
var archiveRoot = new Folder("Archive", "system");

await folders.InsertRootAsync(liveRoot, liveTreeId, cancellationToken);
await folders.InsertRootAsync(archiveRoot, archiveTreeId, cancellationToken);

var documents = new Folder("Documents", "folder");
await folders.InsertChildAsync(documents, liveRoot.Id, cancellationToken);
```

Both roots initially have bounds `1..2`. After inserting Documents, the live
root has bounds `1..4` while the archive root remains `1..2`. The trees remain
distinct because every row stores `TreeId` and every structural query includes
it.

Start from any node without loading it first:

```csharp
var matchingAncestor = await folders
    .AncestorsOf(documents.Id)
    .Where(folder => folder.Type == "system")
    .OrderByDescending(folder => folder.Depth)
    .FirstOrDefaultAsync(cancellationToken);

var liveTree = await folders
    .InTree(liveTreeId)
    .Nodes
    .ToListAsync(cancellationToken);
```

The anchor, its TreeId, and its bounds are resolved inside the generated SQL.
Queries are no-tracking and composable by default.

## Update ordering through SaveChanges

The configured Name/Type order applies within each sibling group. Updating an
order property through normal tracked EF code repositions the whole subtree:

```csharp
var folder = await context.Folders
    .SingleAsync(candidate => candidate.Id == documents.Id, cancellationToken);

folder.Name = "Archive";
await context.SaveChangesAsync(cancellationToken);
```

Payload and structure use one lock and transaction boundary. The tracked
structural values are refreshed before the save is accepted.

## Validate and repair

Maintenance is always bound to one complete tree identity:

```csharp
var tree = folders.InTree(liveTreeId);
var report = await tree.ValidateAsync(
    NestedSetValidationLevel.Full,
    cancellationToken);

if (!report.IsValid)
{
    var plan = await tree.PlanRebuildAsync(cancellationToken);

    if (plan.CanRebuild)
    {
        await tree.RebuildAsync(cancellationToken);
    }
}
```

Rebuild treats ParentId and sibling order as canonical. It does not invent a
missing parent or repair application domain relationships.

## Create a migration

Use ordinary provider migrations:

```bash
dotnet ef migrations add AddFolderHierarchy
dotnet ef database update
```

Review the structural columns, checks, scoped self-FK, derived indexes, and the
typed tree-registry table. SafeMigrations is optional.

Continue with [Hierarchy model](hierarchy-model.md),
[Queries and mutations](operations.md), and
[Transactions and locking](transactions-and-locking.md).

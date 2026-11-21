namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class ConfiguredMutableIdentityTests
{
    /// <summary>Registers one application callback at the selected active stage after fixture seeding.</summary>
    private static void ObserveSave(
        DbContext context,
        bool afterSave,
        Action callback
    )
    {
        if (afterSave)
        {
            context.SavedChanges += (_, _) => callback();
        }
        else
        {
            context.SavingChanges += (_, _) => callback();
        }
    }

    /// <summary>Creates distinctive caller values whose exact restoration cannot be mistaken for defaults.</summary>
    private static ConfiguredIdentityNode OriginalInput() => new()
    {
        Id = new MutableIdentity("input"),
        Scope = new MutableIdentity("original-scope"),
        TreeId = new MutableIdentity("original-tree"),
        ParentId = new MutableIdentity("original-parent"),
        Left = 71,
        Right = 72,
        Depth = 17,
        Position = 23,
    };

    /// <summary>Captures mutable representations by value rather than retaining caller-owned objects.</summary>
    private static IdentitySnapshot Snapshot(
        ConfiguredIdentityNode node
    ) => new(
        node.Id.Value,
        node.Scope.Value,
        node.TreeId.Value,
        node.ParentId?.Value,
        node.Left,
        node.Right,
        node.Depth,
        node.Position);

    /// <summary>Reads exact persisted structure independently of the operation's tracker state.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task<IdentitySnapshot[]> RowsAsync(
        DbContext context,
        CancellationToken cancellationToken
    )
    {
        var rows = await context
            .Set<ConfiguredIdentityNode>()
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);

        return rows
            .OrderBy(row => row.Id.Value, StringComparer.Ordinal)
            .Select(Snapshot)
            .ToArray();
    }

    /// <summary>Captures registry identity, revision and lifecycle to detect hidden callback writes.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task<RegistrySnapshot[]> RegistriesAsync(
        DbContext context,
        CancellationToken cancellationToken
    )
    {
        var entityType = context.Model.FindEntityType(typeof(ConfiguredIdentityNode))!;
        var registry = NestedSetTreeRegistryMapping.For(entityType)
            .Registry;

        var rows = await context
            .Set<NestedSetTreeRegistry>(registry.Name)
            .AsNoTracking()
            .Select(row => new
            {
                TreeId = EF.Property<MutableIdentity>(row, NestedSetTreeRegistryMetadata.TreeId),
                Revision = EF.Property<long>(row, NestedSetTreeRegistryMetadata.Revision),
                Lifecycle = EF.Property<byte>(row, NestedSetTreeRegistryMetadata.Lifecycle),
            })
            .ToArrayAsync(cancellationToken);

        return rows
            .OrderBy(row => row.TreeId.Value, StringComparer.Ordinal)
            .Select(row => new RegistrySnapshot(row.TreeId.Value, row.Revision, row.Lifecycle))
            .ToArray();
    }
}

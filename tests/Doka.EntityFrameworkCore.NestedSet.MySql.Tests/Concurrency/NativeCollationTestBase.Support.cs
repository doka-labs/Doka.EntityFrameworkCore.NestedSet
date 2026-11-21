namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

public abstract partial class NativeCollationTestBase
{
    /// <summary>Captures complete mutable tracker values without retaining the converted Scope instance.</summary>
    private static (string Scope, int Id, Guid TreeId, int? Parent, long Left, long Right, int Depth, long Position,
        string Name) ScopeSnapshot(
            BroadScopeNode node
        )
        => (node.Scope.Value, node.Id, node.TreeId, node.ParentId,
            node.Left, node.Right, node.Depth, node.Position, node.Name);

    /// <summary>Reads every persisted value in this test's independently assigned node-key range.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    /// <param name="root">The synthetic root node.</param>
    private static async Task<(string Scope, int Id, Guid TreeId, int? Parent, long Left, long Right, int Depth,
        long Position, string Name)[]> BroadScopeRowsAsync(
        DbContext context,
        int root,
        CancellationToken cancellationToken
    )
    {
        var rows = await context
            .Set<BroadScopeNode>()
            .AsNoTracking()
            .Where(node => node.Id >= root && node.Id <= root + 1)
            .OrderBy(node => node.Id)
            .ToArrayAsync(cancellationToken);

        return rows
            .Select(ScopeSnapshot)
            .ToArray();
    }

    /// <summary>Captures all persisted registry identity and lifecycle fields for the selected tree.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    /// <param name="treeId">The hierarchy tree identity.</param>
    private static async Task<(string Scope, Guid TreeId, long Revision, byte Lifecycle)[]> BroadScopeRegistriesAsync(
        DbContext context,
        Guid treeId,
        CancellationToken cancellationToken
    )
    {
        var entityType = context.Model.FindEntityType(typeof(BroadScopeNode))!;
        var registry = NestedSetTreeRegistryMapping.For(entityType)
            .Registry;

        var rows = await context
            .Set<NestedSetTreeRegistry>(registry.Name)
            .AsNoTracking()
            .Where(row => EF.Property<Guid>(row, NestedSetTreeRegistryMetadata.TreeId) == treeId)
            .Select(row => new
            {
                Scope = EF.Property<BroadScope>(row, NestedSetTreeRegistryMetadata.Scope),
                TreeId = EF.Property<Guid>(row, NestedSetTreeRegistryMetadata.TreeId),
                Revision = EF.Property<long>(row, NestedSetTreeRegistryMetadata.Revision),
                Lifecycle = EF.Property<byte>(row, NestedSetTreeRegistryMetadata.Lifecycle),
            })
            .ToArrayAsync(cancellationToken);

        return rows
            .OrderBy(row => row.Scope.Value, StringComparer.Ordinal)
            .Select(row => (row.Scope.Value, row.TreeId, row.Revision, row.Lifecycle))
            .ToArray();
    }
}

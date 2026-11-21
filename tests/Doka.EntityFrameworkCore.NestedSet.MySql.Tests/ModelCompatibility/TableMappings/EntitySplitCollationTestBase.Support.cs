namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

public abstract partial class EntitySplitCollationTestBase
{
    /// <summary>Reads complete persisted entity values without introducing tracked state.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    /// <param name="scope">The hierarchy scope used by the scenario.</param>
    /// <param name="treeId">The hierarchy tree identity.</param>
    private static async Task<(string Id, string Scope, Guid TreeId, string? Parent, long Left, long Right, int Depth,
        long Position, string Name, string Payload)[]> TreeSnapshotsAsync(
        EntitySplitCollationContext context,
        string scope,
        Guid treeId,
        CancellationToken cancellationToken
    )
    {
        var nodes = await context
            .NestedSet<EntitySplitCollationNode>()
            .ForScope(scope)
            .InTree(treeId)
            .Nodes
            .ToArrayAsync(cancellationToken);

        return nodes
            .Select(Snapshot)
            .ToArray();
    }

    /// <summary>Captures registry identity, revision, and lifecycle for exact failed-mutation comparison.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    /// <param name="treeId">The hierarchy tree identity.</param>
    private static Task<(string Scope, Guid TreeId, long Revision, byte Lifecycle)[]> RegistrySnapshotsAsync(
        EntitySplitCollationContext context,
        Guid treeId,
        CancellationToken cancellationToken
    )
    {
        var hierarchy = context.Model.FindEntityType(typeof(EntitySplitCollationNode))!;
        var registry = NestedSetTreeRegistryMapping.For(hierarchy)
            .Registry;

        return context
            .Set<NestedSetTreeRegistry>(registry.Name)
            .AsNoTracking()
            .Where(row => EF.Property<Guid>(row, NestedSetTreeRegistryMetadata.TreeId) == treeId)
            .OrderBy(row => EF.Property<string>(row, NestedSetTreeRegistryMetadata.Scope))
            .Select(row => new ValueTuple<string, Guid, long, byte>(
                EF.Property<string>(row, NestedSetTreeRegistryMetadata.Scope),
                EF.Property<Guid>(row, NestedSetTreeRegistryMetadata.TreeId),
                EF.Property<long>(row, NestedSetTreeRegistryMetadata.Revision),
                EF.Property<byte>(row, NestedSetTreeRegistryMetadata.Lifecycle)))
            .ToArrayAsync(cancellationToken);
    }
}

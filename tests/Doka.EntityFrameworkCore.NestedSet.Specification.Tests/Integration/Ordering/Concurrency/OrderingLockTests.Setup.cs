namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingLockTests
{
    /// <summary>Creates one valid root and child in an explicitly identified tree.</summary>
    private static async Task SeedPairAsync<TScope, THierarchy>(
        OrderingLockContext context,
        TScope scope,
        Guid treeId,
        int start,
        CancellationToken cancellationToken
    )
        where TScope : notnull
    {
        var hierarchy = context
            .NestedSet<OrderingLockNode<TScope, THierarchy>>()
            .ForScope(scope);

        await hierarchy.InsertRootAsync(
            new OrderingLockNode<TScope, THierarchy>
            {
                Id = start + 1,
                Scope = scope,
                Name = "Root",
            },
            treeId,
            cancellationToken);

        await hierarchy.InsertChildAsync(
            new OrderingLockNode<TScope, THierarchy>
            {
                Id = start + 2,
                Scope = scope,
                Name = "Alpha",
            },
            start + 1,
            cancellationToken);
    }

    /// <summary>Removes one infrastructure row to prove coordinated saves require an active registry.</summary>
    private static async Task DeleteRegistryAsync(
        OrderingLockContext context,
        int scope,
        Guid treeId,
        CancellationToken cancellationToken
    )
    {
        var entityType = context.Model.FindEntityType(typeof(OrderingLockNode<int, FirstLockHierarchy>))!;
        var mapping = NestedSetMapping<OrderingLockNode<int, FirstLockHierarchy>, int, int>.For(context, entityType);
        var registry = NestedSetTreeRegistryMapping.For(mapping.EntityType)
            .Registry;

        var deleted = await context
            .Set<NestedSetTreeRegistry>(registry.Name)
            .Where(row => EF.Property<int>(row, "Scope") == scope && EF.Property<Guid>(row, "TreeId") == treeId)
            .ExecuteDeleteAsync(cancellationToken);

        Assert.Equal(1, deleted);
    }

    /// <summary>Checks exact persisted structure after both coordinated saves complete.</summary>
    private static async Task AssertPairAsync<TScope, THierarchy>(
        OrderingLockContext context,
        TScope scope,
        Guid treeId,
        int start,
        CancellationToken cancellationToken
    )
        where TScope : notnull
    {
        var nodes = await context
            .NestedSet<OrderingLockNode<TScope, THierarchy>>()
            .ForScope(scope)
            .InTree(treeId)
            .Nodes
            .ToArrayAsync(cancellationToken);

        Assert.Equal([start + 1, start + 2], nodes.Select(node => node.Id));
        Assert.Equal(["Root", "Zulu"], nodes.Select(node => node.Name));
        Assert.Equal([0, 0], nodes.Select(node => node.Position));
        Assert.Equal([1, 2], nodes.Select(node => node.Left));
        Assert.Equal([4, 3], nodes.Select(node => node.Right));
        Assert.Equal([0, 1], nodes.Select(node => node.Depth));
        Assert.Null(nodes[0].ParentId);
        Assert.Equal(start + 1, nodes[1].ParentId);
    }

    /// <summary>Reads Scope parameters from server registry-lifecycle locking queries.</summary>
    private static int[] IntegerRegistryScopes(
        OrderingLockProbe probe
    ) => OrderingLockTestSupport
        .RegistryLifecycleLocks(probe)
        .Select(command => Assert.IsType<int>(command.Parameters[0].Value))
        .ToArray();
}

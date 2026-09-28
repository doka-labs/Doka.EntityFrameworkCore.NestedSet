namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class AutomaticMoveBudgetTests
{
    /// <summary>
    ///     Seeds two parent groups and a nonleaf source without requiring a depth-increase overflow query.
    /// </summary>
    private static async Task SeedAsync(
        OrderingContext context
    )
    {
        var tree = context
            .NestedSet<OrderingNode>()
            .ForScope(1);
        // WHY: A surviving root keeps both tested destinations in one TreeId while exposing two sibling depths.
        await tree.InsertRootAsync(
            new OrderingNode
            {
                Id = 0,
                Name = "Container",
            },
            Guid.Empty,
            CancellationToken.None);

        await tree.InsertChildAsync(
            new OrderingNode
            {
                Id = 1,
                Name = "Alpha",
            },
            0,
            CancellationToken.None);

        await tree.InsertChildAsync(
            new OrderingNode
            {
                Id = 2,
                Name = "Zulu",
            },
            0,
            CancellationToken.None);

        await tree.InsertChildAsync(
            new OrderingNode
            {
                Id = 3,
                Name = "Middle",
            },
            1,
            CancellationToken.None);

        await tree.InsertChildAsync(
            new OrderingNode
            {
                Id = 4,
                Name = "Nested",
            },
            3,
            CancellationToken.None);

        await tree.InsertChildAsync(
            new OrderingNode
            {
                Id = 5,
                Name = "Alpha",
            },
            2,
            CancellationToken.None);

        await tree.InsertChildAsync(
            new OrderingNode
            {
                Id = 6,
                Name = "Zulu",
            },
            2,
            CancellationToken.None);
    }

    /// <summary>Counts structural reads after acquiring the exact-tree registry lock.</summary>
    private static string[] LockedHierarchyReads(
        EnterpriseProbe probe,
        string mode,
        OrderingContext context
    ) => probe
        .Commands
        .SkipWhile(sql => !NestedSetTestInfrastructure.ReferencesRegistry(context, sql))
        .Where(sql => IsHierarchyRead(sql, mode))
        .ToArray();

    /// <summary>Separates public anchor resolution from the locked structural algorithm.</summary>
    private static string[] FacadeIdentityReads(
        EnterpriseProbe probe,
        string mode,
        OrderingContext context
    ) => probe
        .Commands
        .TakeWhile(sql => !NestedSetTestInfrastructure.ReferencesRegistry(context, sql))
        .Where(sql => IsHierarchyRead(sql, mode))
        .ToArray();

    /// <summary>Recognizes hierarchy SELECTs without including registry commands.</summary>
    private static bool IsHierarchyRead(
        string sql,
        string mode
    ) => sql
            .TrimStart()
            .StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
        && sql.Contains(mode + "OrderingNodes", StringComparison.Ordinal);

    /// <summary>Distinguishes structural table writes from successful infrastructure lock acquisition.</summary>
    private static bool IsHierarchyWrite(
        string sql
    ) => sql
            .TrimStart()
            .StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
        && sql.Contains("OrderingNodes", StringComparison.Ordinal);

    /// <summary>Compares every node's persisted scope, ancestry, boundaries, depth, and position.</summary>
    private static async Task<string[]> SnapshotAsync(
        OrderingContext context
    )
    {
        var nodes = await context
            .Set<OrderingNode>()
            .AsNoTracking()
            .OrderBy(node => node.Id)
            .Select(node => new
            {
                node.Id,
                node.Scope,
                node.TreeId,
                node.ParentId,
                node.Left,
                node.Right,
                node.Depth,
                node.Position,
            })
            .ToArrayAsync(CancellationToken.None);

        // WHY: Format identities after materialization so binary Guid storage never becomes a SQL text cast.
        return nodes
            .Select(node => $"{node.Id}:{node.Scope}:{node.TreeId}:{node.ParentId}:"
                + $"{node.Left}:{node.Right}:{node.Depth}:{node.Position}")
            .ToArray();
    }

    /// <summary>Verifies canonical parent links and whole-subtree geometry after either placement policy.</summary>
    private static async Task AssertMoveAsync(
        OrderingContext context,
        bool upperGroup,
        bool automatic
    )
    {
        var tree = context
            .NestedSet<OrderingNode>()
            .ForScope(1);

        var siblings = await tree
            .ChildrenOf(upperGroup ? 0 : 2)
            .Select(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        int[] expected = upperGroup
            ? automatic ? [1, 3, 2] : [1, 2, 3]
            : automatic
                ? [5, 3, 6]
                : [5, 6, 3];

        Assert.Equal(expected, siblings);
        var subtree = await tree
            .SubtreeOf(3)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal([3, 4], subtree.Select(node => node.Id));
        Assert.Equal(upperGroup ? 0 : 2, subtree[0].ParentId);
        Assert.Equal(3, subtree[1].ParentId);
        Assert.Equal(upperGroup ? 1 : 2, subtree[0].Depth);
        Assert.Equal(upperGroup ? 2 : 3, subtree[1].Depth);
        Assert.Equal(automatic ? 1 : 2, subtree[0].Position);
        Assert.Equal(0, subtree[1].Position);
        Assert.True(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }
}

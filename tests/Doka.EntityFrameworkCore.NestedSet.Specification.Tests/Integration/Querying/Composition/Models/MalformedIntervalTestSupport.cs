namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Shares malformed split fixtures between generic ancestor and Doka subtree regressions.</summary>
internal static class MalformedIntervalTestSupport
{
    // WHY: Both consumers use the same fixture-cached entity-split table. Reserve disjoint keys
    // independently of ordinary mapping probes so their retained rows cannot collide.
    private static int s_identity = 2_000_000;

    /// <summary>Reserves enough scope-independent node identities for one complete malformed fixture.</summary>
    internal static int NextIdentity()
        => Interlocked.Add(ref s_identity, 20);

    /// <summary>Includes valid, shared, malformed and extreme representable signed intervals.</summary>
    internal static (long Left, long Right)[] Bounds()
        =>
        [
            (1, 10), (3, 4), (3, 4), (3, 3), (4, 3), (3, 7), (1, 4), (4, 5),
            (long.MinValue, long.MaxValue),
            (long.MinValue, long.MinValue + 1),
            (long.MaxValue - 1, long.MaxValue),
            (long.MaxValue, long.MaxValue),
            (long.MaxValue, long.MinValue),
        ];

    /// <summary>Imports representable split intervals and foreign-tree adversaries without hierarchy repair.</summary>
    internal static async Task<List<EntitySplitNode>> SeedAsync(
        DbContext context,
        int first,
        Guid tree,
        Guid foreignTree,
        (long Left, long Right)[] bounds
    )
    {
        var nodes = bounds.Select((pair, offset) => new EntitySplitNode
        {
            Id = first + offset,
            TreeId = tree,
            Left = pair.Left,
            Right = pair.Right,
            Name = $"interval-{offset}",
        }).ToList();

        nodes.Add(new EntitySplitNode { Id = first + bounds.Length, TreeId = foreignTree, Left = 1, Right = 10 });
        nodes.Add(new EntitySplitNode { Id = first + bounds.Length + 1, TreeId = foreignTree, Left = 3, Right = 4 });
        await context.AddRangeAsync(nodes, CancellationToken.None);
        // WHY: Entity splitting has no cross-fragment width CHECK. Precomputed import deliberately
        // supplies representable malformed intervals, so the oracle cannot rely on valid-tree assumptions.
        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();

        return nodes;
    }
}

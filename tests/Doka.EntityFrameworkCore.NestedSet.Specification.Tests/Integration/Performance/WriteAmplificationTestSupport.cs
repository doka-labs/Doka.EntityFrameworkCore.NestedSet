namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Seeds the common interval workload without coupling provider-local cases to shared test suites.</summary>
internal static class WriteAmplificationTestSupport
{
    /// <summary>Seeds a local branching subtree, a long unchanged tail, and an overlapping isolated scope.</summary>
    internal static async Task SeedAsync(
        TestDatabase database
    )
    {
        await using var context = database.CreateContext();
        await context.AddRangeAsync(
        [
            Node(1, 1, 12, null, 0, 0),
            Node(2, 2, 5, 1, 1, 0),
            Node(3, 3, 4, 2, 2, 0),
            Node(4, 6, 9, 1, 1, 1),
            Node(5, 7, 8, 4, 2, 0),
            Node(6, 10, 11, 1, 1, 2),
        ], CancellationToken.None);

        for (var key = 7; key <= 107; key++)
        {
            await context.AddAsync(Node(key, (key * 2) - 1, key * 2, null, 0, key - 6), CancellationToken.None);
        }

        // WHY: Independent TreeIds have one root. A permanent enclosing root preserves the original local
        // interval and distant-subtree workload while adding exactly one enclosing-bound write for size changes.
        foreach (var entry in context.ChangeTracker.Entries<TreeNode>())
        {
            var node = entry.Entity;
            node.Start++;
            node.End++;
            node.Depth++;
            node.Parent ??= 900000;
        }

        await context.AddAsync(Node(900000, 1, 216, null, 0, 0), CancellationToken.None);
        await context.AddAsync(
            new TreeNode
            {
                NodeId = 1000,
                Tree = 2,
                Start = 1,
                End = 2,
            },
            CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
    }

    /// <summary>Creates a node with deterministic coordinates for row-count measurements.</summary>
    private static TreeNode Node(
        int key,
        long left,
        long right,
        int? parent,
        int depth,
        long position
    ) => new()
    {
        NodeId = key,
        Tree = 1,
        Start = left,
        End = right,
        Parent = parent,
        Depth = depth,
        Position = position,
    };
}

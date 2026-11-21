namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class RelationalTests
{
    /// <summary>Seeds two roots and a second scope whose valid coordinates overlap the first tree.</summary>
    /// <param name="database">The reset database receiving independently known structural values.</param>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    /// <returns>A task that completes after the seed context has saved and released its tracked entities.</returns>
    private static async Task SeedTreeQueryForestAsync(
        TestDatabase database,
        CancellationToken cancellationToken
    )
    {
        // WHY: Direct coordinates isolate query behavior from mutation correctness.
        // Keys deliberately differ from preorder so an incidental primary-key sort cannot pass.
        var nodes = new[]
        {
            new TreeNode
            {
                NodeId = 10,
                Tree = 1,
                Start = 1,
                End = 12,
                Payload = "hidden",
            },
            new TreeNode
            {
                NodeId = 70,
                Tree = 1,
                Start = 2,
                End = 7,
                Parent = 10,
                Depth = 1,
                Payload = "hidden",
            },
            new TreeNode
            {
                NodeId = 90,
                Tree = 1,
                Start = 3,
                End = 4,
                Parent = 70,
                Depth = 2,
                Payload = "match",
            },
            new TreeNode
            {
                NodeId = 20,
                Tree = 1,
                Start = 5,
                End = 6,
                Parent = 70,
                Depth = 2,
                Position = 1,
                Payload = "match",
            },
            new TreeNode
            {
                NodeId = 30,
                Tree = 1,
                Start = 8,
                End = 11,
                Parent = 10,
                Depth = 1,
                Position = 1,
                Payload = "match",
            },
            new TreeNode
            {
                NodeId = 40,
                Tree = 1,
                Start = 9,
                End = 10,
                Parent = 30,
                Depth = 2,
                Payload = "match",
            },
            new TreeNode
            {
                NodeId = 200,
                Tree = 1,
                TreeId = new Guid("00000000-0000-0000-0000-000000000002"),
                Start = 1,
                End = 4,
                Payload = "match",
            },
            new TreeNode
            {
                NodeId = 210,
                Tree = 1,
                TreeId = new Guid("00000000-0000-0000-0000-000000000002"),
                Start = 2,
                End = 3,
                Parent = 200,
                Depth = 1,
                Payload = "match",
            },
        };

        // WHY: Matching payload and intervals in another scope challenge every anchor, root, and result filter.
        var foreignNodes = nodes
            .Take(6)
            .Select(node => new TreeNode
            {
                NodeId = node.NodeId + 1000,
                Tree = 2,
                Start = node.Start,
                End = node.End,
                Parent = node.Parent + 1000,
                Depth = node.Depth,
                Position = node.Position,
                Payload = "match",
            });

        await using var context = database.CreateContext();
        await context.AddRangeAsync(nodes.Concat(foreignNodes), cancellationToken);
        await context.SavePrecomputedHierarchyAsync(cancellationToken);
    }

    /// <summary>Seeds binary-key branches plus unrelated roots in the same and another scope.</summary>
    /// <param name="database">The reset database whose existing model supports binary primary keys.</param>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    /// <returns>A task that completes after storing the independent binary-key forest.</returns>
    private static async Task SeedBinaryTreeQueryForestAsync(
        TestDatabase database,
        CancellationToken cancellationToken
    )
    {
        await using var context = database.CreateContext();
        await context.AddRangeAsync(
            [
                new BinaryNode
                {
                    Id = [1, 2],
                    Tree = 1,
                    Left = 1,
                    Right = 8,
                },
                new BinaryNode
                {
                    Id = [3, 4],
                    Tree = 1,
                    Left = 2,
                    Right = 5,
                    ParentId = [1, 2],
                    Depth = 1,
                },
                new BinaryNode
                {
                    Id = [5, 6],
                    Tree = 1,
                    Left = 3,
                    Right = 4,
                    ParentId = [3, 4],
                    Depth = 2,
                },
                new BinaryNode
                {
                    Id = [7, 8],
                    Tree = 1,
                    Left = 6,
                    Right = 7,
                    ParentId = [1, 2],
                    Depth = 1,
                    Position = 1,
                },
                new BinaryNode
                {
                    Id = [9, 10],
                    Tree = 1,
                    TreeId = new Guid("00000000-0000-0000-0000-000000000002"),
                    Left = 1,
                    Right = 2,
                },
                new BinaryNode
                {
                    Id = [11, 12],
                    Tree = 2,
                    Left = 1,
                    Right = 4,
                },
                new BinaryNode
                {
                    Id = [13, 14],
                    Tree = 2,
                    Left = 2,
                    Right = 3,
                    ParentId = [11, 12],
                    Depth = 1,
                },
            ],
            cancellationToken);

        await context.SavePrecomputedHierarchyAsync(cancellationToken);
    }
}

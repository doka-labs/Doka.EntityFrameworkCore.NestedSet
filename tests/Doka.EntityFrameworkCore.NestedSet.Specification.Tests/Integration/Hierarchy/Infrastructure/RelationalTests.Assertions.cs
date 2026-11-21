namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class RelationalTests
{
    /// <summary>Compares the complete ordered result of a hierarchy query.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task AssertOrderAsync(
        IQueryable<TreeNode> query,
        CancellationToken cancellationToken,
        params int[] expected
    ) => Assert.Equal(
        expected,
        await query
            .Select(x => x.NodeId)
            .ToArrayAsync(cancellationToken));

    /// <summary>Checks selected depth values while keeping all assertions in the assertion phase.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task AssertDepthsAsync(
        TreeContext context,
        int scope,
        CancellationToken cancellationToken,
        params (int Id, int Depth)[] expected
    )
    {
        foreach (var (id, depth) in expected)
        {
            var actual = await context
                .Set<TreeNode>()
                .AsNoTracking()
                .Where(node => node.Tree == scope)
                .Where(x => x.NodeId == id)
                .Select(x => x.Depth)
                .SingleAsync(cancellationToken);

            Assert.Equal(depth, actual);
        }
    }

    /// <summary>
    ///     Checks validation output and independently recomputes interval, parent, depth and position invariants.
    /// </summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task AssertValidAsync(
        TreeContext context,
        int scope,
        CancellationToken cancellationToken
    )
    {
        var nodes = await context
            .Set<TreeNode>()
            .AsNoTracking()
            .Where(node => node.Tree == scope)
            .OrderBy(node => node.Start)
            .ToArrayAsync(cancellationToken);

        // WHY: Bounds and positions are local to TreeId. Equal coordinates in independent trees are intentional.
        foreach (var group in nodes.GroupBy(node => node.TreeId))
        {
            var treeNodes = group
                .OrderBy(node => node.Start)
                .ToArray();

            var report = await context
                .NestedSet<TreeNode>()
                .ForScope(scope)
                .InTree(group.Key)
                .ValidateAsync(NestedSetValidationLevel.Full, cancellationToken);

            Assert.True(report.IsValid, string.Join("; ", report.Issues.Select(issue => issue.Message)));
            Assert.Single(treeNodes, node => node.Parent == null);
            Assert.Equal(
                Enumerable
                    .Range(1, treeNodes.Length * 2)
                    .Select(value => (long)value),
                treeNodes
                    .SelectMany(node => new[] { node.Start, node.End })
                    .Order());

            foreach (var node in treeNodes)
            {
                var ancestors = treeNodes
                    .Where(parent => parent.Start < node.Start && parent.End > node.End)
                    .ToArray();

                Assert.True(node.Start < node.End);
                Assert.Equal(0, (node.End - node.Start + 1) % 2);
                Assert.Equal(
                    ancestors.LastOrDefault()
                        ?.NodeId,
                    node.Parent);

                Assert.Equal(ancestors.Length, node.Depth);
            }

            foreach (var siblings in treeNodes.GroupBy(node => node.Parent))
            {
                Assert.Equal(
                    Enumerable
                        .Range(0, siblings.Count())
                        .Select(value => (long)value),
                    siblings.Select(node => node.Position));
            }
        }
    }
}

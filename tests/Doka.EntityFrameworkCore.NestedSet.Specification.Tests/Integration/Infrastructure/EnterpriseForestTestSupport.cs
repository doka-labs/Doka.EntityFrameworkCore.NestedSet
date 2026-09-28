namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Provides canonical forest setup shared by provider-independent and native plan specifications.</summary>
internal static class EnterpriseForestTestSupport
{
    /// <summary>Creates canonical adjacency and coordinates without measuring hierarchy insertion costs.</summary>
    internal static TreeNode[] CreateForest(
        int count,
        bool deep = false
    ) => Enumerable
        .Range(0, count)
        .Select(index => new TreeNode
        {
            NodeId = index + 1,
            Tree = 1,
            Parent = index == 0
                ? null
                : deep
                    ? index
                    : 1,
            Start = deep
                ? index + 1
                : index == 0
                    ? 1
                    : index * 2,
            End = deep
                ? (count * 2) - index
                : index == 0
                    ? count * 2
                    : (index * 2) + 1,
            Depth = deep
                ? index
                : index == 0
                    ? 0
                    : 1,
            Position = deep || index == 0 ? 0 : index - 1,
            Payload = new string('x', 4096),
        })
        .ToArray();

    /// <summary>Persists fixture data and releases setup-owned tracker entries before an observed operation.</summary>
    internal static async Task SeedForestAsync(
        TreeContext context,
        TreeNode[] nodes,
        bool corrupt = false
    )
    {
        await context.AddRangeAsync(nodes, CancellationToken.None);
        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();

        if (corrupt)
        {
            await context
                .Set<TreeNode>()
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(node => node.Start, 1)
                        .SetProperty(node => node.End, 2)
                        .SetProperty(node => node.Depth, 0),
                    CancellationToken.None);
        }
    }
}

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class CapacityTests
{
    /// <summary>Warms the same public import, inspection, and repair paths before measuring capacity work.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task WarmAsync(
        TestDatabase database,
        CancellationToken cancellationToken
    )
    {
        await using var context = CreateCapacityContext(database);
        await context
            .NestedSet<TreeNode>()
            .ForScope(1)
            .InsertForestAsync(
                [new NestedSetTreeImport<TreeNode, Guid>(Guid.Empty, CreateWide(65))],
                cancellationToken);

        await context
            .Set<TreeNode>()
            .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.Start, 1L), cancellationToken);

        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1)
            .InTree(Guid.Empty);

        await tree.RebuildAsync(cancellationToken);
        await tree.ValidateAsync(NestedSetValidationLevel.Full, cancellationToken);
        await database.ResetAsync(cancellationToken: cancellationToken);
    }

    /// <summary>Builds all caller-owned topology and application payload before the measured baseline.</summary>
    private static NestedSetBranch<TreeNode> CreateWide(
        int count
    )
    {
        var children = new NestedSetBranch<TreeNode>[count - 1];
        for (var index = 0; index < children.Length; index++)
        {
            children[index] = new NestedSetBranch<TreeNode>(CreateInput(index + 2));
        }

        return new NestedSetBranch<TreeNode>(CreateInput(1), children);
    }

    /// <summary>Gives each input original structure so rollback cannot pass through default values.</summary>
    private static TreeNode CreateInput(
        int key
    ) => new()
    {
        NodeId = key,
        TreeId = s_inputTreeId,
        Tree = 7,
        Start = 17,
        End = 18,
        Depth = 9,
        Position = 11,
        Parent = 999,
        Payload = "caller-owned capacity payload",
    };
}

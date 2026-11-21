namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class RelationalTests
{
    /// <summary>Seeds siblings and a grandchild without performing assertions during Arrange.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task SeedPlacementTreeAsync(
        TreeContext context,
        int scope,
        CancellationToken cancellationToken
    )
    {
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(scope);

        await tree.InsertRootAsync(Node(1), Guid.Empty, cancellationToken);
        await tree.InsertChildAsync(Node(2), 1, cancellationToken: cancellationToken);
        await tree.InsertAsFirstChildAsync(Node(3), 1, cancellationToken);
        await tree.InsertBeforeAsync(Node(4), 2, cancellationToken);
        await tree.InsertAfterAsync(Node(5), 2, cancellationToken);
        await tree.InsertChildAsync(Node(6), 2, cancellationToken: cancellationToken);
    }

    /// <summary>Seeds two branches whose unequal depths expose subtree-level depth errors.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task SeedDeepTreeAsync(
        TreeContext context,
        int scope,
        CancellationToken cancellationToken
    )
    {
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(scope);

        await tree.InsertRootAsync(Node(1), Guid.Empty, cancellationToken);
        await tree.InsertChildAsync(Node(2), 1, cancellationToken: cancellationToken);
        await tree.InsertChildAsync(Node(3), 2, cancellationToken: cancellationToken);
        await tree.InsertChildAsync(Node(4), 3, cancellationToken: cancellationToken);
        await tree.InsertChildAsync(Node(5), 4, cancellationToken: cancellationToken);
        await tree.InsertRootAsync(Node(6), Guid.NewGuid(), cancellationToken);
        await tree.InsertChildAsync(Node(7), 6, cancellationToken: cancellationToken);
        await tree.InsertChildAsync(Node(8), 7, cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     Seeds branching children so repair checks nonzero positions in multiple sibling groups.
    /// </summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task SeedRebuildTreeAsync(
        TreeContext context,
        int scope,
        CancellationToken cancellationToken
    )
    {
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(scope);

        await tree.InsertRootAsync(Node(1), Guid.Empty, cancellationToken);
        await tree.InsertChildAsync(Node(2), 1, cancellationToken: cancellationToken);
        await tree.InsertChildAsync(Node(3), 1, cancellationToken: cancellationToken);
        await tree.InsertChildAsync(Node(4), 2, cancellationToken: cancellationToken);
        await tree.InsertChildAsync(Node(5), 4, cancellationToken: cancellationToken);
        await tree.InsertChildAsync(Node(6), 1, cancellationToken);
    }

    /// <summary>Seeds multiple child subtrees between surrounding siblings for promotion tests.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task SeedPromotionTreeAsync(
        TreeContext context,
        int scope,
        CancellationToken cancellationToken
    )
    {
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(scope);

        await SeedDeepTreeAsync(context, scope, cancellationToken: cancellationToken);
        await tree.InsertBeforeAsync(Node(9), 3, cancellationToken);
        await tree.InsertAfterAsync(Node(10), 3, cancellationToken);
        await tree.InsertAfterAsync(Node(11), 4, cancellationToken);
        await tree.InsertChildAsync(Node(12), 11, cancellationToken: cancellationToken);
    }

    /// <summary>Seeds a three-node subtree and a deeper destination for executed-depth rollback probes.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task SeedDepthFailureTreeAsync(
        TreeContext context,
        int scope,
        CancellationToken cancellationToken
    )
    {
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(scope);

        await tree.InsertRootAsync(Node(1), Guid.Empty, cancellationToken);
        await tree.InsertChildAsync(Node(2), 1, cancellationToken: cancellationToken);
        await tree.InsertChildAsync(Node(3), 2, cancellationToken: cancellationToken);
        await tree.InsertChildAsync(Node(4), 3, cancellationToken: cancellationToken);
        await tree.InsertRootAsync(Node(6), Guid.NewGuid(), cancellationToken);
        await tree.InsertChildAsync(Node(7), 6, cancellationToken: cancellationToken);
    }

    /// <summary>Seeds a branch whose parent links are protected by an immediate restrictive foreign key.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task SeedConstrainedTreeAsync(
        TreeContext context,
        int scope,
        CancellationToken cancellationToken
    )
    {
        var tree = context
            .NestedSet<ConstrainedNode>()
            .ForScope(scope);

        await tree.InsertRootAsync(new ConstrainedNode { Id = 1 }, Guid.Empty, cancellationToken);
        await tree.InsertChildAsync(new ConstrainedNode { Id = 2 }, 1, cancellationToken: cancellationToken);
        await tree.InsertChildAsync(new ConstrainedNode { Id = 3 }, 2, cancellationToken: cancellationToken);
        await tree.InsertChildAsync(new ConstrainedNode { Id = 4 }, 1, cancellationToken: cancellationToken);
    }
}

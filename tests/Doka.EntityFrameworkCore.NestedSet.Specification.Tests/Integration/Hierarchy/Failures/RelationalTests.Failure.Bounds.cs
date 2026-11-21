namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class RelationalTests
{
    /// <summary>Moving a subtree rejects descendant depth overflow without partial writes.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MoveToAsync_DescendantDepthOverflow_RejectsMove()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedDepthFailureTreeAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);
        await context
            .Set<TreeNode>()
            .AsNoTracking()
            .Where(node => node.Tree == 1)
            .Where(x => x.NodeId == 4)
            .ExecuteUpdateAsync(x => x.SetProperty(n => n.Depth, int.MaxValue), CancellationToken.None);

        var before = await SnapshotAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var error = await Record.ExceptionAsync(() => tree.MoveToAsync(2, 7, CancellationToken.None));

        // Assert
        Assert.IsType<OverflowException>(error);
        Assert.Equal(before, await SnapshotAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>Inserting below the maximum depth cannot wrap the child depth.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task InsertChildAsync_ParentDepthOverflow_RejectsInsert()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedDeepTreeAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);
        await context
            .Set<TreeNode>()
            .AsNoTracking()
            .Where(node => node.Tree == 1)
            .Where(x => x.NodeId == 8)
            .ExecuteUpdateAsync(x => x.SetProperty(n => n.Depth, int.MaxValue), CancellationToken.None);

        var before = await SnapshotAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertChildAsync(
            Node(9),
            8,
            cancellationToken: CancellationToken.None));

        // Assert
        Assert.IsType<OverflowException>(error);
        Assert.Equal(before, await SnapshotAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>The derived database constraint rejects a negative boundary without changing the tree.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ExecuteUpdateAsync_DatabaseConstraint_RejectsNegativeBoundaryWithoutPartialWrites()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await tree.InsertRootAsync(Node(1), Guid.Empty, CancellationToken.None);
        var before = await SnapshotAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var error = await Record.ExceptionAsync(() => context
            .Set<TreeNode>()
            .AsNoTracking()
            .Where(node => node.Tree == 1)
            .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.Start, -1), CancellationToken.None));

        // Assert
        Assert.IsAssignableFrom<DbException>(error);
        Assert.Equal(before, await SnapshotAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>A full coordinate range is rejected instead of wrapping boundaries.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task InsertChildAsync_BoundaryOverflow_RejectsInsert()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        await tree.InsertRootAsync(Node(1), Guid.Empty, CancellationToken.None);
        await context
            .Set<TreeNode>()
            .AsNoTracking()
            .Where(node => node.Tree == 1)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(node => node.End, long.MaxValue - 1),
                CancellationToken.None);

        var before = await SnapshotAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertChildAsync(Node(2), 1, CancellationToken.None));

        // Assert
        Assert.IsType<OverflowException>(error);
        Assert.Equal(before, await SnapshotAsync(context, 1, cancellationToken: TestContext.Current.CancellationToken));
    }
}

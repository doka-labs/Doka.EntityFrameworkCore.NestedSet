namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingTests
{
    /// <summary>Verifies rename ordering does not disturb identical data in another forest scope.</summary>
    [Fact]
    public async Task SaveChangesAsync_Rename_LeavesAnotherScopeUnchanged()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        await SeedAsync(Tree(context), cancellationToken: TestContext.Current.CancellationToken);
        var other = Tree(context, 2);
        await other.InsertRootAsync(Node(11, "Root"), Guid.NewGuid(), CancellationToken.None);
        await other.InsertChildAsync(Node(12, "Zulu"), 11, CancellationToken.None);
        await other.InsertChildAsync(Node(13, "Alpha"), 11, CancellationToken.None);
        var before = await Nodes(context, 2)
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        var renamed = await context
            .Set<OrderingNode>()
            .SingleAsync(node => node.Id == 3, CancellationToken.None);

        renamed.Name = "Zulu";

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        await using var verification = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        AssertSnapshot(
            before,
            await Nodes(verification, 2)
                .OrderBy(node => node.Id)
                .ToArrayAsync(CancellationToken.None));

        await AssertIdsAsync(
            Tree(verification)
                .ChildrenOf(1),
            cancellationToken: TestContext.Current.CancellationToken,
            2,
            4,
            3);

        await AssertForestAsync(verification, cancellationToken: TestContext.Current.CancellationToken, scope: 1);
        await AssertForestAsync(verification, cancellationToken: TestContext.Current.CancellationToken, scope: 2);
    }

    /// <summary>
    ///     Reorders one TreeId without changing a tracked tree with overlapping bounds in the same Scope.
    /// </summary>
    [Fact]
    public async Task SaveChangesAsync_Rename_LeavesAnotherTreeInTheSameScopeUnchanged()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        var hierarchy = context
            .NestedSet<OrderingNode>()
            .ForScope(1);

        var changedTreeId = Guid.NewGuid();
        var unchangedTreeId = Guid.NewGuid();
        await hierarchy.InsertRootAsync(Node(1, "Changed root"), changedTreeId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(2, "Alpha"), 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(3, "Bravo"), 1, CancellationToken.None);
        await hierarchy.InsertRootAsync(Node(10, "Unchanged root"), unchangedTreeId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(11, "Alpha"), 10, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(12, "Bravo"), 10, CancellationToken.None);
        context.ChangeTracker.Clear();
        var tracked = await context
            .Set<OrderingNode>()
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        var unchangedBefore = tracked
            .Where(node => node.TreeId == unchangedTreeId)
            .Select(node => (node.Id, node.Left, node.Right, node.Depth, node.Position))
            .ToArray();

        tracked.Single(node => node.Id == 2)
            .Name = "Zulu";

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        var unchangedAfter = tracked
            .Where(node => node.TreeId == unchangedTreeId)
            .Select(node => (node.Id, node.Left, node.Right, node.Depth, node.Position))
            .ToArray();

        var changedOrder = new[] { 3, 2 };
        var unchangedOrder = new[] { 11, 12 };

        Assert.Equal(unchangedBefore, unchangedAfter);
        await using var verification = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        Assert.Equal(
            changedOrder,
            await verification
                .Set<OrderingNode>()
                .Where(node => node.TreeId == changedTreeId && node.ParentId == 1)
                .OrderBy(node => node.Position)
                .Select(node => node.Id)
                .ToArrayAsync(CancellationToken.None));

        Assert.Equal(
            unchangedOrder,
            await verification
                .Set<OrderingNode>()
                .Where(node => node.TreeId == unchangedTreeId && node.ParentId == 10)
                .OrderBy(node => node.Position)
                .Select(node => node.Id)
                .ToArrayAsync(CancellationToken.None));
    }
}

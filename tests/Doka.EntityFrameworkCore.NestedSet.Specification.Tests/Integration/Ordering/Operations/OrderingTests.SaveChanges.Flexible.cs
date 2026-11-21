namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingTests
{
    /// <summary>
    ///     Verifies automatic rename placement preserves the relative order of manually placed unchanged nodes.
    /// </summary>
    [Fact]
    public async Task SaveChangesAsync_FlexibleRename_PreservesUnchangedManualOrder()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Flexible");

        var tree = Tree(context);
        await SeedAsync(tree, cancellationToken: TestContext.Current.CancellationToken);
        await tree.MoveBeforeAsync(4, 2, CancellationToken.None);
        var renamed = await context
            .Set<OrderingNode>()
            .SingleAsync(node => node.Id == 3, CancellationToken.None);

        renamed.Name = "Aaron";

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        await using var verification = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Flexible");

        await AssertIdsAsync(
            Tree(verification)
                .ChildrenOf(1),
            cancellationToken: TestContext.Current.CancellationToken,
            3,
            4,
            2);

        await AssertIdsAsync(
            Tree(verification)
                .SubtreeOf(3),
            cancellationToken: TestContext.Current.CancellationToken,
            3,
            5);

        await AssertForestAsync(verification, cancellationToken: TestContext.Current.CancellationToken, scope: 1);
    }

    /// <summary>Verifies multiple changed nodes do not reset manual ordering of their unchanged siblings.</summary>
    [Fact]
    public async Task SaveChangesAsync_FlexibleMultipleRenames_RetainUnchangedRelativeOrder()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Flexible");

        var tree = Tree(context);
        await SeedAsync(tree, cancellationToken: TestContext.Current.CancellationToken);
        await tree.InsertChildAsync(Node(6, "Delta"), 1, CancellationToken.None);
        await tree.MoveBeforeAsync(4, 2, CancellationToken.None);
        var nodes = await context
            .Set<OrderingNode>()
            .ToDictionaryAsync(node => node.Id, CancellationToken.None);

        nodes[3].Name = "Aaron";
        nodes[6].Name = "Zulu";

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        await using var verification = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Flexible");

        // WHY: Zulu follows its rule predecessor Charlie while Charlie retains its manual position before Alpha.
        await AssertIdsAsync(
            Tree(verification)
                .ChildrenOf(1),
            cancellationToken: TestContext.Current.CancellationToken,
            3,
            4,
            6,
            2);

        await AssertIdsAsync(
            Tree(verification)
                .ChildrenOf(1)
                .Where(node => node.Id == 4 || node.Id == 2),
            cancellationToken: TestContext.Current.CancellationToken,
            4,
            2);

        await AssertForestAsync(verification, cancellationToken: TestContext.Current.CancellationToken, scope: 1);
    }

    /// <summary>
    ///     Verifies an unrelated payload edit does not silently sort a manually overridden sibling group.
    /// </summary>
    [Fact]
    public async Task SaveChangesAsync_PayloadOnlySave_PreservesManualPlacementWithoutStructuralUpdates()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Flexible");

        await SeedAsync(Tree(setup), cancellationToken: TestContext.Current.CancellationToken);
        await Tree(setup)
            .MoveBeforeAsync(4, 2, CancellationToken.None);

        var probe = new OrderingSaveProbe();
        await using var context = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Flexible",
            interceptors: [probe]);

        var changed = await context
            .Set<OrderingNode>()
            .SingleAsync(node => node.Id == 2, CancellationToken.None);

        changed.Payload = "changed without renaming";

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, probe.CompletedPayloadUpdates);
        Assert.Equal(0, probe.StructuralUpdates);
        await using var verification = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Flexible");

        await AssertIdsAsync(
            Tree(verification)
                .ChildrenOf(1),
            cancellationToken: TestContext.Current.CancellationToken,
            4,
            2,
            3);

        Assert.Equal(
            "changed without renaming",
            await verification
                .Set<OrderingNode>()
                .Where(node => node.Id == 2)
                .Select(node => node.Payload)
                .SingleAsync(CancellationToken.None));
    }
}

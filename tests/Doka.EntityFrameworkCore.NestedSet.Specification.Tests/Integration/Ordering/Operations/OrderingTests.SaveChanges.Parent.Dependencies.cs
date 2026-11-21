namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingTests
{
    /// <summary>Applies a valid dependent Parent plan independently of entity tracking order.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveChangesAsync_ParentChanges_ApplyDependenciesBeforeTheirSources(
        bool reverseTracking
    )
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        var hierarchy = context
            .NestedSet<OrderingNode>()
            .ForScope(1);

        var treeId = Guid.NewGuid();
        await hierarchy.InsertRootAsync(Node(1, "Root"), treeId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(2, "A"), 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(3, "B"), 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(4, "C"), 3, CancellationToken.None);
        context.ChangeTracker.Clear();
        var query = context
            .Set<OrderingNode>()
            .Where(node => node.Id == 3 || node.Id == 4);

        var tracked = reverseTracking
            ? await query
                .OrderByDescending(node => node.Id)
                .ToArrayAsync(CancellationToken.None)
            : await query
                .OrderBy(node => node.Id)
                .ToArrayAsync(CancellationToken.None);

        tracked.Single(node => node.Id == 3)
            .ParentId = 4;

        tracked.Single(node => node.Id == 4)
            .ParentId = 2;

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        await using var verification = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        var nodes = await verification
            .Set<OrderingNode>()
            .OrderBy(node => node.Left)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal([1, 2, 4, 3], nodes.Select(node => node.Id));
        Assert.Equal(
            4,
            nodes.Single(node => node.Id == 3)
                .ParentId);

        Assert.Equal(
            2,
            nodes.Single(node => node.Id == 4)
                .ParentId);

        Assert.Equal(
            3,
            nodes.Single(node => node.Id == 3)
                .Depth);

        await AssertForestAsync(verification, cancellationToken: TestContext.Current.CancellationToken, scope: 1);
    }

    /// <summary>Orders dependent Parent changes even when their destination changes trees first.</summary>
    [Fact]
    public async Task SaveChangesAsync_ParentChanges_FollowTargetAcrossTrees()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        var hierarchy = context
            .NestedSet<OrderingNode>()
            .ForScope(1);

        var firstTree = Guid.NewGuid();
        var secondTree = Guid.NewGuid();
        await hierarchy.InsertRootAsync(Node(1, "Root"), firstTree, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(2, "A"), 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(3, "B"), 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(4, "C"), 3, CancellationToken.None);
        await hierarchy.InsertRootAsync(Node(10, "Destination"), secondTree, CancellationToken.None);
        context.ChangeTracker.Clear();
        var changed = await context
            .Set<OrderingNode>()
            .Where(node => node.Id == 3 || node.Id == 4)
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        changed.Single(node => node.Id == 3)
            .ParentId = 4;

        changed.Single(node => node.Id == 4)
            .ParentId = 10;

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        await using var verification = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        var nodes = await verification
            .Set<OrderingNode>()
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(
            secondTree,
            nodes.Single(node => node.Id == 3)
                .TreeId);

        Assert.Equal(
            secondTree,
            nodes.Single(node => node.Id == 4)
                .TreeId);

        Assert.Equal(
            4,
            nodes.Single(node => node.Id == 3)
                .ParentId);

        Assert.Equal(
            10,
            nodes.Single(node => node.Id == 4)
                .ParentId);

        foreach (var treeId in new[] { firstTree, secondTree })
        {
            var bounds = nodes
                .Where(node => node.TreeId == treeId)
                .SelectMany(node => new[] { node.Left, node.Right })
                .OrderBy(value => value)
                .ToArray();

            Assert.Equal(
                Enumerable
                    .Range(1, bounds.Length)
                    .Select(value => (long)value),
                bounds);
        }
    }

    /// <summary>Resolves a chain of nested move dependencies from the deepest target outward.</summary>
    [Fact]
    public async Task SaveChangesAsync_ParentChanges_ResolveNestedDependencyChain()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        var hierarchy = context
            .NestedSet<OrderingNode>()
            .ForScope(1);

        await hierarchy.InsertRootAsync(Node(1, "Root"), Guid.NewGuid(), CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(2, "A"), 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(3, "B"), 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(4, "C"), 3, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(5, "D"), 4, CancellationToken.None);
        context.ChangeTracker.Clear();
        var changed = await context
            .Set<OrderingNode>()
            .Where(node => node.Id >= 3)
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        changed.Single(node => node.Id == 3)
            .ParentId = 4;

        changed.Single(node => node.Id == 4)
            .ParentId = 5;

        changed.Single(node => node.Id == 5)
            .ParentId = 2;

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        await using var verification = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        var nodes = await verification
            .Set<OrderingNode>()
            .OrderBy(node => node.Left)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal([1, 2, 5, 4, 3], nodes.Select(node => node.Id));
        Assert.Equal([0, 1, 2, 3, 4], nodes.Select(node => node.Depth));
        await AssertForestAsync(verification, cancellationToken: TestContext.Current.CancellationToken, scope: 1);
    }

    /// <summary>Rejects a cyclic final Parent plan and rolls back the accompanying payload update.</summary>
    [Fact]
    public async Task SaveChangesAsync_CyclicParentPlan_RollsBackPayload()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        var hierarchy = context
            .NestedSet<OrderingNode>()
            .ForScope(1);

        await hierarchy.InsertRootAsync(Node(1, "Root"), Guid.NewGuid(), CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(2, "A"), 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(3, "B"), 1, CancellationToken.None);
        context.ChangeTracker.Clear();
        var changed = await context
            .Set<OrderingNode>()
            .Where(node => node.Id == 2 || node.Id == 3)
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        changed.Single(node => node.Id == 2)
            .ParentId = 3;

        changed.Single(node => node.Id == 3)
            .ParentId = 2;

        changed.Single(node => node.Id == 2)
            .Payload = "must-rollback";

        // Act
        var failure = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));

        // Assert
        Assert.Equal(
            NestedSetErrorCode.CycleDetected,
            Assert.IsType<NestedSetException>(failure)
                .Code);

        await using var verification = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        var nodes = await verification
            .Set<OrderingNode>()
            .Where(node => node.Id == 2 || node.Id == 3)
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        Assert.All(nodes, node => Assert.Equal(1, node.ParentId));
        Assert.Equal("original", nodes[0].Payload);
    }
}

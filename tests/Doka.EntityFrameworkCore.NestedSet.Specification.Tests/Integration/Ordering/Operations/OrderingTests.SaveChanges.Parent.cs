namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingTests
{
    /// <summary>Applies a valid dependent Parent plan independently of entity tracking order.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParentChangesApplyDependenciesBeforeTheirSources(bool reverseTracking)
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var hierarchy = context.NestedSet<OrderingNode>().ForScope(1);
        var treeId = Guid.NewGuid();
        await hierarchy.InsertRootAsync(Node(1, "Root"), treeId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(2, "A"), 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(3, "B"), 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(4, "C"), 3, CancellationToken.None);
        context.ChangeTracker.Clear();
        var query = context.Set<OrderingNode>().Where(node => node.Id == 3 || node.Id == 4);
        var tracked = reverseTracking
            ? await query.OrderByDescending(node => node.Id).ToArrayAsync(CancellationToken.None)
            : await query.OrderBy(node => node.Id).ToArrayAsync(CancellationToken.None);

        tracked.Single(node => node.Id == 3).ParentId = 4;
        tracked.Single(node => node.Id == 4).ParentId = 2;

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        await using var verification = await _fixture.CreateContextAsync(Engine);
        var nodes = await verification.Set<OrderingNode>()
            .OrderBy(node => node.Left)
            .ToArrayAsync(CancellationToken.None);
        Assert.Equal([1, 2, 4, 3], nodes.Select(node => node.Id));
        Assert.Equal(4, nodes.Single(node => node.Id == 3).ParentId);
        Assert.Equal(2, nodes.Single(node => node.Id == 4).ParentId);
        Assert.Equal(3, nodes.Single(node => node.Id == 3).Depth);
        await AssertForestAsync(verification);
    }

    /// <summary>Orders dependent Parent changes even when their destination changes trees first.</summary>
    [Fact]
    public async Task ParentChangesFollowTargetAcrossTrees()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var hierarchy = context.NestedSet<OrderingNode>().ForScope(1);
        var firstTree = Guid.NewGuid();
        var secondTree = Guid.NewGuid();
        await hierarchy.InsertRootAsync(Node(1, "Root"), firstTree, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(2, "A"), 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(3, "B"), 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(4, "C"), 3, CancellationToken.None);
        await hierarchy.InsertRootAsync(Node(10, "Destination"), secondTree, CancellationToken.None);
        context.ChangeTracker.Clear();
        var changed = await context.Set<OrderingNode>()
            .Where(node => node.Id == 3 || node.Id == 4)
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        changed.Single(node => node.Id == 3).ParentId = 4;
        changed.Single(node => node.Id == 4).ParentId = 10;

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        await using var verification = await _fixture.CreateContextAsync(Engine);
        var nodes = await verification.Set<OrderingNode>()
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(secondTree, nodes.Single(node => node.Id == 3).TreeId);
        Assert.Equal(secondTree, nodes.Single(node => node.Id == 4).TreeId);
        Assert.Equal(4, nodes.Single(node => node.Id == 3).ParentId);
        Assert.Equal(10, nodes.Single(node => node.Id == 4).ParentId);

        foreach (var treeId in new[] { firstTree, secondTree })
        {
            var bounds = nodes.Where(node => node.TreeId == treeId)
                .SelectMany(node => new[] { node.Left, node.Right })
                .OrderBy(value => value)
                .ToArray();

            Assert.Equal(Enumerable.Range(1, bounds.Length).Select(value => (long)value), bounds);
        }
    }

    /// <summary>Resolves a chain of nested move dependencies from the deepest target outward.</summary>
    [Fact]
    public async Task ParentChangesResolveNestedDependencyChain()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var hierarchy = context.NestedSet<OrderingNode>().ForScope(1);
        await hierarchy.InsertRootAsync(Node(1, "Root"), Guid.NewGuid(), CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(2, "A"), 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(3, "B"), 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(4, "C"), 3, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(5, "D"), 4, CancellationToken.None);
        context.ChangeTracker.Clear();
        var changed = await context.Set<OrderingNode>()
            .Where(node => node.Id >= 3)
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        changed.Single(node => node.Id == 3).ParentId = 4;
        changed.Single(node => node.Id == 4).ParentId = 5;
        changed.Single(node => node.Id == 5).ParentId = 2;

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        await using var verification = await _fixture.CreateContextAsync(Engine);
        var nodes = await verification.Set<OrderingNode>()
            .OrderBy(node => node.Left)
            .ToArrayAsync(CancellationToken.None);
        Assert.Equal([1, 2, 5, 4, 3], nodes.Select(node => node.Id));
        Assert.Equal([0, 1, 2, 3, 4], nodes.Select(node => node.Depth));
        await AssertForestAsync(verification);
    }

    /// <summary>Rejects a cyclic final Parent plan and rolls back the accompanying payload update.</summary>
    [Fact]
    public async Task CyclicParentPlanRollsBackPayload()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var hierarchy = context.NestedSet<OrderingNode>().ForScope(1);
        await hierarchy.InsertRootAsync(Node(1, "Root"), Guid.NewGuid(), CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(2, "A"), 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(3, "B"), 1, CancellationToken.None);
        context.ChangeTracker.Clear();
        var changed = await context.Set<OrderingNode>()
            .Where(node => node.Id == 2 || node.Id == 3)
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        changed.Single(node => node.Id == 2).ParentId = 3;
        changed.Single(node => node.Id == 3).ParentId = 2;
        changed.Single(node => node.Id == 2).Payload = "must-rollback";

        // Act
        var failure = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.CycleDetected, Assert.IsType<NestedSetException>(failure).Code);
        await using var verification = await _fixture.CreateContextAsync(Engine);
        var nodes = await verification.Set<OrderingNode>()
            .Where(node => node.Id == 2 || node.Id == 3)
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);
        Assert.All(nodes, node => Assert.Equal(1, node.ParentId));
        Assert.Equal("original", nodes[0].Payload);
    }

    /// <summary>Moves a tracked subtree to another Parent and preserves its descendants in one save.</summary>
    [Fact]
    public async Task ParentChangeMovesTrackedSubtree()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var hierarchy = context.NestedSet<OrderingNode>().ForScope(1);
        var treeId = Guid.NewGuid();
        await hierarchy.InsertRootAsync(Node(1, "Root"), treeId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(2, "Alpha"), 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(3, "Bravo"), 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(4, "Leaf"), 2, CancellationToken.None);
        context.ChangeTracker.Clear();
        var changed = await context.Set<OrderingNode>()
            .SingleAsync(node => node.Id == 2, CancellationToken.None);

        changed.ParentId = 3;

        // Act
        var saved = await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.Equal(0, saved);
        await using var verification = await _fixture.CreateContextAsync(Engine);
        var nodes = await verification.Set<OrderingNode>()
            .OrderBy(node => node.Left)
            .ToArrayAsync(CancellationToken.None);

        var expectedOrder = new[] { 1, 3, 2, 4 };

        Assert.Equal(expectedOrder, nodes.Select(node => node.Id));
        Assert.Equal(3, nodes.Single(node => node.Id == 2).ParentId);
        Assert.Equal(2, nodes.Single(node => node.Id == 2).Depth);
        Assert.Equal(3, nodes.Single(node => node.Id == 4).Depth);
        Assert.All(nodes, node => Assert.Equal(treeId, node.TreeId));
        await AssertForestAsync(verification);
    }

    /// <summary>Moves a tracked subtree between trees and updates every descendant TreeId atomically.</summary>
    [Fact]
    public async Task ParentChangeMovesTrackedSubtreeAcrossTrees()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var hierarchy = context.NestedSet<OrderingNode>().ForScope(1);
        var sourceTreeId = Guid.NewGuid();
        var targetTreeId = Guid.NewGuid();
        await hierarchy.InsertRootAsync(Node(1, "Source"), sourceTreeId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(2, "Branch"), 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(3, "Leaf"), 2, CancellationToken.None);
        await hierarchy.InsertRootAsync(Node(10, "Target"), targetTreeId, CancellationToken.None);
        context.ChangeTracker.Clear();
        var changed = await context.Set<OrderingNode>()
            .SingleAsync(node => node.Id == 2, CancellationToken.None);

        changed.ParentId = 10;

        // Act
        var saved = await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.Equal(0, saved);
        await using var verification = await _fixture.CreateContextAsync(Engine);
        var nodes = await verification.Set<OrderingNode>()
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);
        Assert.Equal(sourceTreeId, nodes.Single(node => node.Id == 1).TreeId);
        Assert.Equal(targetTreeId, nodes.Single(node => node.Id == 2).TreeId);
        Assert.Equal(targetTreeId, nodes.Single(node => node.Id == 3).TreeId);
        Assert.Equal(10, nodes.Single(node => node.Id == 2).ParentId);
        Assert.Equal(1, nodes.Single(node => node.Id == 2).Depth);
        Assert.Equal(2, nodes.Single(node => node.Id == 3).Depth);
        Assert.Equal(new long[] { 1, 2 }, nodes.Where(node => node.TreeId == sourceTreeId)
            .SelectMany(node => new[] { node.Left, node.Right })
            .OrderBy(value => value));
        Assert.Equal(new long[] { 1, 2, 3, 4, 5, 6 }, nodes.Where(node => node.TreeId == targetTreeId)
            .SelectMany(node => new[] { node.Left, node.Right })
            .OrderBy(value => value));
    }

    /// <summary>Rejects using a nullable Parent change as an implicit tree-detach operation.</summary>
    [Fact]
    public async Task ParentChangeToNullRequiresExplicitDetach()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var hierarchy = context.NestedSet<OrderingNode>().ForScope(1);
        var treeId = Guid.NewGuid();
        await hierarchy.InsertRootAsync(Node(1, "Root"), treeId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(2, "Child"), 1, CancellationToken.None);
        context.ChangeTracker.Clear();
        var changed = await context.Set<OrderingNode>()
            .SingleAsync(node => node.Id == 2, CancellationToken.None);

        changed.ParentId = null;

        // Act
        var failure = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));

        // Assert
        var rejection = Assert.IsType<NestedSetException>(failure);
        Assert.Equal(NestedSetErrorCode.OperationRejected, rejection.Code);
        Assert.Contains(nameof(NestedSet<OrderingNode>.DetachAsTreeAsync),
            rejection.Message,
            StringComparison.Ordinal);
        Assert.Equal(1, await context.Set<OrderingNode>()
            .Where(node => node.Id == 2)
            .Select(node => node.ParentId)
            .SingleAsync(CancellationToken.None));
    }

    /// <summary>Rolls back a tracked Parent change that would create a hierarchy cycle.</summary>
    [Fact]
    public async Task ParentChangeRejectsCycle()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var hierarchy = context.NestedSet<OrderingNode>().ForScope(1);
        var treeId = Guid.NewGuid();
        await hierarchy.InsertRootAsync(Node(1, "Root"), treeId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(2, "Child"), 1, CancellationToken.None);
        context.ChangeTracker.Clear();
        var changed = await context.Set<OrderingNode>()
            .SingleAsync(node => node.Id == 1, CancellationToken.None);

        changed.ParentId = 2;

        // Act
        var failure = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));

        // Assert
        var rejection = Assert.IsType<NestedSetException>(failure);
        Assert.Equal(NestedSetErrorCode.CycleDetected, rejection.Code);
        var persisted = await context.Set<OrderingNode>()
            .AsNoTracking()
            .OrderBy(node => node.Left)
            .ToArrayAsync(CancellationToken.None);
        var expectedOrder = new[] { 1, 2 };

        Assert.Equal(expectedOrder, persisted.Select(node => node.Id));
        Assert.Null(persisted[0].ParentId);
        Assert.Equal(1, persisted[1].ParentId);
    }

    /// <summary>Rolls back payload and Parent movement together and leaves the same context retryable.</summary>
    [Fact]
    public async Task ParentChangeRollsBackWithPayloadAndCanRetry()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        var hierarchy = setup.NestedSet<OrderingNode>().ForScope(1);
        var treeId = Guid.NewGuid();
        await hierarchy.InsertRootAsync(Node(1, "Root"), treeId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(2, "Alpha"), 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(3, "Bravo"), 1, CancellationToken.None);
        var probe = new OrderingSaveProbe { FailAfterPayload = true };
        await using var context = await _fixture.CreateContextAsync(Engine, "Strict", probe);
        var changed = await context.Set<OrderingNode>()
            .SingleAsync(node => node.Id == 2, CancellationToken.None);

        changed.ParentId = 3;
        changed.Payload = "moved payload";

        // Act
        var failure = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        probe.FailAfterPayload = false;
        var retryFailure = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));

        // Assert
        Assert.IsType<OrderingInjectedException>(failure);
        Assert.Null(retryFailure);
        await using var verification = await _fixture.CreateContextAsync(Engine);
        var persisted = await verification.Set<OrderingNode>()
            .SingleAsync(node => node.Id == 2, CancellationToken.None);
        Assert.Equal(3, persisted.ParentId);
        Assert.Equal("moved payload", persisted.Payload);
        Assert.Equal(EntityState.Unchanged, context.Entry(changed).State);
        await AssertForestAsync(verification);
    }
}

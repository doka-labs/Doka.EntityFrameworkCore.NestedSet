namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingTests
{
    /// <summary>Moves a tracked subtree to another Parent and preserves its descendants in one save.</summary>
    [Fact]
    public async Task SaveChangesAsync_ParentChange_MovesTrackedSubtree()
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
        await hierarchy.InsertChildAsync(Node(2, "Alpha"), 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(3, "Bravo"), 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(4, "Leaf"), 2, CancellationToken.None);
        context.ChangeTracker.Clear();
        var changed = await context
            .Set<OrderingNode>()
            .SingleAsync(node => node.Id == 2, CancellationToken.None);

        changed.ParentId = 3;

        // Act
        var saved = await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.Equal(0, saved);
        await using var verification = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        var nodes = await verification
            .Set<OrderingNode>()
            .OrderBy(node => node.Left)
            .ToArrayAsync(CancellationToken.None);

        var expectedOrder = new[] { 1, 3, 2, 4 };

        Assert.Equal(expectedOrder, nodes.Select(node => node.Id));
        Assert.Equal(
            3,
            nodes.Single(node => node.Id == 2)
                .ParentId);

        Assert.Equal(
            2,
            nodes.Single(node => node.Id == 2)
                .Depth);

        Assert.Equal(
            3,
            nodes.Single(node => node.Id == 4)
                .Depth);

        Assert.All(nodes, node => Assert.Equal(treeId, node.TreeId));
        await AssertForestAsync(verification, cancellationToken: TestContext.Current.CancellationToken, scope: 1);
    }

    /// <summary>Moves a tracked subtree between trees and updates every descendant TreeId atomically.</summary>
    [Fact]
    public async Task SaveChangesAsync_ParentChange_MovesTrackedSubtreeAcrossTrees()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        var hierarchy = context
            .NestedSet<OrderingNode>()
            .ForScope(1);

        var sourceTreeId = Guid.NewGuid();
        var targetTreeId = Guid.NewGuid();
        await hierarchy.InsertRootAsync(Node(1, "Source"), sourceTreeId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(2, "Branch"), 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(3, "Leaf"), 2, CancellationToken.None);
        await hierarchy.InsertRootAsync(Node(10, "Target"), targetTreeId, CancellationToken.None);
        context.ChangeTracker.Clear();
        var changed = await context
            .Set<OrderingNode>()
            .SingleAsync(node => node.Id == 2, CancellationToken.None);

        changed.ParentId = 10;

        // Act
        var saved = await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.Equal(0, saved);
        await using var verification = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        var nodes = await verification
            .Set<OrderingNode>()
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(
            sourceTreeId,
            nodes.Single(node => node.Id == 1)
                .TreeId);

        Assert.Equal(
            targetTreeId,
            nodes.Single(node => node.Id == 2)
                .TreeId);

        Assert.Equal(
            targetTreeId,
            nodes.Single(node => node.Id == 3)
                .TreeId);

        Assert.Equal(
            10,
            nodes.Single(node => node.Id == 2)
                .ParentId);

        Assert.Equal(
            1,
            nodes.Single(node => node.Id == 2)
                .Depth);

        Assert.Equal(
            2,
            nodes.Single(node => node.Id == 3)
                .Depth);

        Assert.Equal(
            new long[] { 1, 2 },
            nodes
                .Where(node => node.TreeId == sourceTreeId)
                .SelectMany(node => new[] { node.Left, node.Right })
                .OrderBy(value => value));

        Assert.Equal(
            new long[]
            {
                1,
                2,
                3,
                4,
                5,
                6,
            },
            nodes
                .Where(node => node.TreeId == targetTreeId)
                .SelectMany(node => new[] { node.Left, node.Right })
                .OrderBy(value => value));
    }

    /// <summary>Rejects using a nullable Parent change as an implicit tree-detach operation.</summary>
    [Fact]
    public async Task SaveChangesAsync_ParentChangeToNull_RequiresExplicitDetach()
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
        await hierarchy.InsertChildAsync(Node(2, "Child"), 1, CancellationToken.None);
        context.ChangeTracker.Clear();
        var changed = await context
            .Set<OrderingNode>()
            .SingleAsync(node => node.Id == 2, CancellationToken.None);

        changed.ParentId = null;

        // Act
        var failure = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));

        // Assert
        var rejection = Assert.IsType<NestedSetException>(failure);
        Assert.Equal(NestedSetErrorCode.OperationRejected, rejection.Code);
        Assert.Contains(
            nameof(NestedSet<OrderingNode>.DetachAsTreeAsync),
            rejection.Message,
            StringComparison.Ordinal);

        Assert.Equal(
            1,
            await context
                .Set<OrderingNode>()
                .Where(node => node.Id == 2)
                .Select(node => node.ParentId)
                .SingleAsync(CancellationToken.None));
    }

    /// <summary>Rolls back a tracked Parent change that would create a hierarchy cycle.</summary>
    [Fact]
    public async Task SaveChangesAsync_ParentChange_RejectsCycle()
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
        await hierarchy.InsertChildAsync(Node(2, "Child"), 1, CancellationToken.None);
        context.ChangeTracker.Clear();
        var changed = await context
            .Set<OrderingNode>()
            .SingleAsync(node => node.Id == 1, CancellationToken.None);

        changed.ParentId = 2;

        // Act
        var failure = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));

        // Assert
        var rejection = Assert.IsType<NestedSetException>(failure);
        Assert.Equal(NestedSetErrorCode.CycleDetected, rejection.Code);
        var persisted = await context
            .Set<OrderingNode>()
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
    public async Task SaveChangesAsync_ParentChange_RollsBackWithPayloadAndCanRetry()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        var hierarchy = setup
            .NestedSet<OrderingNode>()
            .ForScope(1);

        var treeId = Guid.NewGuid();
        await hierarchy.InsertRootAsync(Node(1, "Root"), treeId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(2, "Alpha"), 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(3, "Bravo"), 1, CancellationToken.None);
        var probe = new OrderingSaveProbe { FailAfterPayload = true };
        await using var context = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict",
            interceptors: [probe]);

        var changed = await context
            .Set<OrderingNode>()
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
        await using var verification = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        var persisted = await verification
            .Set<OrderingNode>()
            .SingleAsync(node => node.Id == 2, CancellationToken.None);

        Assert.Equal(3, persisted.ParentId);
        Assert.Equal("moved payload", persisted.Payload);
        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(changed)
                .State);

        await AssertForestAsync(verification, cancellationToken: TestContext.Current.CancellationToken, scope: 1);
    }
}

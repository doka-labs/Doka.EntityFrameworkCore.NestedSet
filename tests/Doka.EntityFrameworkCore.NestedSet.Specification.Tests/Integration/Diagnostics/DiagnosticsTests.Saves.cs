namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class DiagnosticsTests
{
    /// <summary>Normal SaveChanges reordering has its own operation measurement and lock latency.</summary>
    [Fact]
    public async Task SaveChangesAsync_CoordinatedSave_IsObservedOnce()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        var service = context
            .NestedSet<OrderingNode>()
            .ForScope(1);

        await service.InsertRootAsync(
            new OrderingNode
            {
                Id = 1,
                Name = "Before",
            },
            Guid.Empty,
            TestContext.Current.CancellationToken);

        var node = await context
            .Set<OrderingNode>()
            .SingleAsync(TestContext.Current.CancellationToken);

        node.Name = "private-updated-name";
        using var capture = new DiagnosticsCapture();

        // Act
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        var operation = Assert.Single(
            capture.Activities,
            activity => activity.OperationName == "nestedset.save_changes");

        Assert.Equal(ActivityStatusCode.Ok, operation.Status);
        Assert.Equal(
            1,
            Assert.Single(capture.Measurements, item => item.Name == "nestedset.operation.count")
                .Value);

        Assert.Single(capture.Measurements, item => item.Name == "nestedset.lock.wait.duration");
        AssertPrivate(capture, "save_changes", "lock", "success");
    }

    /// <summary>A managed Parent move contributes rows to one public SaveChanges operation.</summary>
    [Fact]
    public async Task SaveChangesAsync_ParentSave_DoesNotCountItsInternalMove()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        var hierarchy = context
            .NestedSet<OrderingNode>()
            .ForScope(1);

        await hierarchy.InsertRootAsync(
            new OrderingNode
            {
                Id = 1,
                Name = "Root",
            },
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        await hierarchy.InsertChildAsync(
            new OrderingNode
            {
                Id = 2,
                Name = "A",
            },
            1,
            TestContext.Current.CancellationToken);

        await hierarchy.InsertChildAsync(
            new OrderingNode
            {
                Id = 3,
                Name = "B",
            },
            1,
            TestContext.Current.CancellationToken);

        context.ChangeTracker.Clear();
        var moved = await context
            .Set<OrderingNode>()
            .SingleAsync(node => node.Id == 2, TestContext.Current.CancellationToken);

        moved.ParentId = 3;
        using var capture = new DiagnosticsCapture();

        // Act
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(capture.Activities, activity => activity.OperationName == "nestedset.save_changes");
        Assert.DoesNotContain(capture.Activities, activity => activity.OperationName == "nestedset.move_to");
        Assert.Equal(
            1,
            Assert.Single(capture.Measurements, item => item.Name == "nestedset.operation.count")
                .Value);

        Assert.True(
            Assert.Single(capture.Measurements, item => item.Name == "nestedset.rows.affected")
                .Value
            > 0);
    }

    /// <summary>An unrelated native EF save does not claim a hierarchy operation occurred.</summary>
    [Fact]
    public async Task SaveChangesAsync_UnrelatedNativeSave_DoesNotEmitHierarchyMetrics()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        context.Add(new OrderingMarker { Id = 1 });
        using var capture = new DiagnosticsCapture();

        // Act
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(capture.Activities);
        Assert.Empty(capture.Measurements);
    }
}

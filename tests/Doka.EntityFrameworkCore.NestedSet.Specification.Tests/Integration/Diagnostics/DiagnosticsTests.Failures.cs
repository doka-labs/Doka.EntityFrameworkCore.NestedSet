namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class DiagnosticsTests
{
    /// <summary>Rejected operations produce typed errors and one failure count without revealing the key.</summary>
    [Fact]
    public async Task DeleteAsync_MissingNodeFailure_HasBoundedClassification()
    {
        // Arrange
        var database = await _relational.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var service = context
            .NestedSet<TreeNode>()
            .ForScope(934857);

        using var capture = new DiagnosticsCapture();

        // Act
        var error = await Assert.ThrowsAsync<NestedSetException>(() => service.DeleteAsync(
            842653,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(NestedSetErrorCode.NodeNotFound, error.Code);
        var operation = Assert.Single(capture.Activities, activity => activity.OperationName == "nestedset.delete");
        Assert.Equal(ActivityStatusCode.Error, operation.Status);
        Assert.Equal("node_not_found", operation.GetTagItem("error.type"));
        Assert.Equal(
            1,
            Assert.Single(capture.Measurements, item => item.Name == "nestedset.operation.failures")
                .Value);

        AssertPrivate(capture, "delete", "lock", "success", "failure", "node_not_found");
    }

    /// <summary>Canceled requests have a separate outcome without increasing the failure counter.</summary>
    [Fact]
    public async Task InsertRootAsync_Cancellation_HasItsOwnOutcomeAndDoesNotAcquireALock()
    {
        // Arrange
        var database = await _relational.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var service = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var capture = new DiagnosticsCapture();

        // Act
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.InsertRootAsync(new TreeNode { NodeId = 1 }, Guid.Empty, cancellation.Token));

        // Assert
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(
            "canceled",
            Assert
                .Single(capture.Activities)
                .GetTagItem("nestedset.outcome"));

        Assert.Equal(
            1,
            Assert.Single(capture.Measurements, item => item.Name == "nestedset.operation.count")
                .Value);

        Assert.DoesNotContain(capture.Measurements, item => item.Name == "nestedset.operation.failures");
        Assert.DoesNotContain(capture.Measurements, item => item.Name == "nestedset.lock.wait.duration");
        AssertPrivate(capture, "insert_root", "canceled");
    }

    /// <summary>Cancellation inside measured acquisition records wait time and preserves the forest.</summary>
    [Fact]
    public async Task InsertRootAsync_CancellationDuringLockAcquisition_RecordsTheCanceledWait()
    {
        // Arrange
        var database = await _relational.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IInterceptor probe = Engine == "Sqlite"
            ? new LockCancellationProbe(reached)
            : new LockCommandCancellationProbe(reached);

        await using var context = database.CreateContext(probe);
        var service = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var capture = new DiagnosticsCapture();

        // Act
        var operation = service.InsertRootAsync(new TreeNode { NodeId = 1 }, Guid.Empty, cancellation.Token);
        await reached.Task.WaitAsync(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);

        // Assert
        Assert.Equal(cancellation.Token, error.CancellationToken);
        var wait = Assert.Single(capture.Measurements, item => item.Name == "nestedset.lock.wait.duration");
        Assert.True(wait.Value > 0);
        Assert.Contains(wait.Tags, tag => tag is { Key: "nestedset.outcome", Value: "canceled" });
        Assert.DoesNotContain(capture.Measurements, item => item.Name == "nestedset.operation.failures");
        Assert.Empty(
            await service
                .InTree(Guid.Empty)
                .Nodes
                .ToArrayAsync(TestContext.Current.CancellationToken));

        AssertPrivate(capture, "insert_root", "lock", "canceled");
    }

    /// <summary>A rejected lock write emits its bounded library error without recording identifiers.</summary>
    [Fact]
    public async Task InsertRootAsync_UnacquiredWriteLock_HasBoundedClassification()
    {
        // Arrange
        var database = await _relational.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        var probe = new LockCommandFailureProbe();
        await using var context = database.CreateContext(probe);
        var service = context
            .NestedSet<TreeNode>()
            .ForScope(934857);

        using var capture = new DiagnosticsCapture();

        // Act
        var error = await Assert.ThrowsAsync<NestedSetException>(() =>
            service.InsertRootAsync(
                new TreeNode { NodeId = 842653 },
                Guid.Empty,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(NestedSetErrorCode.LockAcquisitionFailed, error.Code);
        var operation = Assert.Single(
            capture.Activities,
            activity => activity.OperationName == "nestedset.insert_root");

        Assert.Equal(ActivityStatusCode.Error, operation.Status);
        Assert.Equal("lock_acquisition_failed", operation.GetTagItem("error.type"));
        Assert.Equal(
            1,
            Assert.Single(capture.Measurements, item => item.Name == "nestedset.operation.count")
                .Value);

        Assert.Equal(
            1,
            Assert.Single(capture.Measurements, item => item.Name == "nestedset.operation.failures")
                .Value);

        Assert.Single(capture.Measurements, item => item.Name == "nestedset.lock.wait.duration");
        AssertPrivate(capture, "insert_root", "lock", "success", "failure", "lock_acquisition_failed");
    }

    /// <summary>A provider error retains its instance while telemetry exposes only the database category.</summary>
    [Fact]
    public async Task InsertRootAsync_ProviderLockFailure_DoesNotExposeItsMessage()
    {
        // Arrange
        var database = await _relational.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        var failure = new LockProviderFailureException();
        var probe = new LockCommandFailureProbe(failure);
        await using var context = database.CreateContext(probe);
        var service = context
            .NestedSet<TreeNode>()
            .ForScope(934857);

        using var capture = new DiagnosticsCapture();

        // Act
        var error = await Assert.ThrowsAsync<LockProviderFailureException>(() =>
            service.InsertRootAsync(
                new TreeNode { NodeId = 842653 },
                Guid.Empty,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Same(failure, error);
        var operation = Assert.Single(
            capture.Activities,
            activity => activity.OperationName == "nestedset.insert_root");

        Assert.Equal(ActivityStatusCode.Error, operation.Status);
        Assert.Equal("database", operation.GetTagItem("error.type"));
        Assert.Equal(
            1,
            Assert.Single(capture.Measurements, item => item.Name == "nestedset.operation.count")
                .Value);

        Assert.Equal(
            1,
            Assert.Single(capture.Measurements, item => item.Name == "nestedset.operation.failures")
                .Value);

        Assert.Single(capture.Measurements, item => item.Name == "nestedset.lock.wait.duration");
        AssertPrivate(capture, "insert_root", "lock", "success", "failure", "database");
    }

    /// <summary>An active-tree purge reports a stable bounded failure classification.</summary>
    [Fact]
    public async Task PurgeTreeIdAsync_ActiveTreePurge_HasBoundedClassification()
    {
        // Arrange
        var database = await _relational.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        var treeId = Guid.Parse("5d9e4a55-9992-4b53-8fa5-11ef36306b6f");
        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, treeId, TestContext.Current.CancellationToken);
        using var capture = new DiagnosticsCapture();

        // Act
        var error = await Assert.ThrowsAsync<NestedSetException>(() =>
            hierarchy.PurgeTreeIdAsync(treeId, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(NestedSetErrorCode.TreeIdNotTombstoned, error.Code);
        var operation = Assert.Single(
            capture.Activities,
            activity => activity.OperationName == "nestedset.purge_tree_id");

        Assert.Equal("tree_id_not_tombstoned", operation.GetTagItem("error.type"));
        AssertPrivate(capture, "purge_tree_id", "lock", "success", "failure", "tree_id_not_tombstoned");
    }
}

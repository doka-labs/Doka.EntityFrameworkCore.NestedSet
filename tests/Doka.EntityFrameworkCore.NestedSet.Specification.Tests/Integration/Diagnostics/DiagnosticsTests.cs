namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks tracing, metrics, and privacy for operations and coordinated saves.</summary>
public abstract class DiagnosticsTests : ProviderTest
{
    private readonly OrderingFixture _fixture;
    private readonly RelationalFixture _relational;

    /// <summary>Creates a test with independently reset ordered tables.</summary>
    /// <param name="fixture">The real relational ordering fixture.</param>
    /// <param name="relational">The reusable unordered hierarchy databases.</param>
    protected DiagnosticsTests(
        IProviderFixture<OrderingFixture> fixture,
        IProviderFixture<RelationalFixture> relational
    ) : base(fixture)
    {
        _fixture = fixture.Value;
        _relational = relational.Value;
    }

    /// <summary>One completed mutation emits one duration/count and a distinct lock-wait measurement.</summary>
    [Fact]
    public async Task SuccessfulMutationMeasuresOperationAndLockWithoutPayloadTags()
    {
        // Arrange
        var database = await _relational.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var service = context
            .NestedSet<TreeNode>()
            .ForScope(934857);

        var node = new TreeNode
        {
            NodeId = 842653,
            Payload = "private-customer-name",
        };

        using var capture = new DiagnosticsCapture();

        // Act
        await service.InsertRootAsync(node, Guid.Empty, CancellationToken.None);

        // Assert
        var operation = Assert.Single(
            capture.Activities,
            activity => activity.OperationName == "nestedset.insert_root");

        var writeLock = Assert.Single(capture.Activities, activity => activity.OperationName == "nestedset.lock");
        Assert.Equal(operation.SpanId, writeLock.ParentSpanId);
        Assert.Equal(ActivityStatusCode.Ok, operation.Status);
        Assert.Equal(ProviderFamily(Engine), operation.GetTagItem("nestedset.provider"));
        Assert.Equal(
            1,
            Assert.Single(capture.Measurements, item => item.Name == "nestedset.operation.count").Value);
        Assert.True(
            Assert.Single(capture.Measurements, item => item.Name == "nestedset.operation.duration").Value > 0);
        Assert.True(
            Assert.Single(capture.Measurements, item => item.Name == "nestedset.lock.wait.duration").Value > 0);
        Assert.Equal(
            1,
            Assert.Single(capture.Measurements, item => item.Name == "nestedset.rows.affected").Value);
        Assert.DoesNotContain(capture.Measurements, item => item.Name == "nestedset.operation.failures");
        AssertPrivate(capture, "insert_root", "lock", "success");
    }

    /// <summary>Rebuild emits exact node, batch, and affected-row measurements without identity tags.</summary>
    [Fact]
    public async Task RebuildPublishesBoundedWorkMeasurements()
    {
        // Arrange
        var database = await _relational.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var treeId = Guid.Parse("8f1f3065-baca-4a03-a179-a6d6d09cf935");
        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, treeId, CancellationToken.None);
        await hierarchy.InsertChildAsync(new TreeNode { NodeId = 2 }, 1, CancellationToken.None);
        _ = await context
            .Set<TreeNode>()
            .Where(node => node.NodeId == 2)
            .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.Depth, 7), CancellationToken.None);

        using var capture = new DiagnosticsCapture();

        // Act
        await hierarchy
            .InTree(treeId)
            .RebuildAsync(CancellationToken.None);

        // Assert
        Assert.Equal(
            2,
            Assert.Single(capture.Measurements, item => item.Name == "nestedset.rebuild.node.count").Value);
        Assert.Equal(
            1,
            Assert.Single(capture.Measurements, item => item.Name == "nestedset.batch.count").Value);
        Assert.Equal(
            1,
            Assert.Single(capture.Measurements, item => item.Name == "nestedset.rows.affected").Value);
        AssertPrivate(capture, "rebuild", "lock", "success");
    }

    /// <summary>Rejected operations produce typed errors and one failure count without revealing the key.</summary>
    [Fact]
    public async Task MissingNodeFailureHasBoundedClassification()
    {
        // Arrange
        var database = await _relational.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var service = context
            .NestedSet<TreeNode>()
            .ForScope(934857);

        using var capture = new DiagnosticsCapture();

        // Act
        var error = await Assert.ThrowsAsync<NestedSetException>(() => service.DeleteAsync(
            842653,
            CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.NodeNotFound, error.Code);
        var operation = Assert.Single(capture.Activities, activity => activity.OperationName == "nestedset.delete");
        Assert.Equal(ActivityStatusCode.Error, operation.Status);
        Assert.Equal("node_not_found", operation.GetTagItem("error.type"));
        Assert.Equal(
            1,
            Assert.Single(capture.Measurements, item => item.Name == "nestedset.operation.failures").Value);
        AssertPrivate(capture, "delete", "lock", "success", "failure", "node_not_found");
    }

    /// <summary>Canceled requests have a separate outcome without increasing the failure counter.</summary>
    [Fact]
    public async Task CancellationHasItsOwnOutcomeAndDoesNotAcquireALock()
    {
        // Arrange
        var database = await _relational.ResetAsync(Engine);
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
            Assert.Single(capture.Measurements, item => item.Name == "nestedset.operation.count").Value);
        Assert.DoesNotContain(capture.Measurements, item => item.Name == "nestedset.operation.failures");
        Assert.DoesNotContain(capture.Measurements, item => item.Name == "nestedset.lock.wait.duration");
        AssertPrivate(capture, "insert_root", "canceled");
    }

    /// <summary>Cancellation inside measured acquisition records wait time and preserves the forest.</summary>
    [Fact]
    public async Task CancellationDuringLockAcquisitionRecordsTheCanceledWait()
    {
        // Arrange
        var database = await _relational.ResetAsync(Engine);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IInterceptor probe = Engine == "Sqlite"
            ? new LockCancellationProbe(reached)
            : new LockCommandCancellationProbe(reached);

        await using var context = database.CreateContext(probe);
        var service = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var capture = new DiagnosticsCapture();

        // Act
        var operation = service.InsertRootAsync(new TreeNode { NodeId = 1 }, Guid.Empty, cancellation.Token);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
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
                .ToArrayAsync(CancellationToken.None));
        AssertPrivate(capture, "insert_root", "lock", "canceled");
    }

    /// <summary>Normal SaveChanges reordering has its own operation measurement and lock latency.</summary>
    [Fact]
    public async Task CoordinatedSaveIsObservedOnce()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
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
            CancellationToken.None);

        var node = await context
            .Set<OrderingNode>()
            .SingleAsync(CancellationToken.None);

        node.Name = "private-updated-name";
        using var capture = new DiagnosticsCapture();

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        var operation = Assert.Single(
            capture.Activities,
            activity => activity.OperationName == "nestedset.save_changes");

        Assert.Equal(ActivityStatusCode.Ok, operation.Status);
        Assert.Equal(
            1,
            Assert.Single(capture.Measurements, item => item.Name == "nestedset.operation.count").Value);
        Assert.Single(capture.Measurements, item => item.Name == "nestedset.lock.wait.duration");
        AssertPrivate(capture, "save_changes", "lock", "success");
    }

    /// <summary>A managed Parent move contributes rows to one public SaveChanges operation.</summary>
    [Fact]
    public async Task ParentSaveDoesNotCountItsInternalMove()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
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
            CancellationToken.None);

        await hierarchy.InsertChildAsync(
            new OrderingNode
            {
                Id = 2,
                Name = "A",
            },
            1,
            CancellationToken.None);

        await hierarchy.InsertChildAsync(
            new OrderingNode
            {
                Id = 3,
                Name = "B",
            },
            1,
            CancellationToken.None);

        context.ChangeTracker.Clear();
        var moved = await context
            .Set<OrderingNode>()
            .SingleAsync(node => node.Id == 2, CancellationToken.None);

        moved.ParentId = 3;
        using var capture = new DiagnosticsCapture();

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.Single(capture.Activities, activity => activity.OperationName == "nestedset.save_changes");
        Assert.DoesNotContain(capture.Activities, activity => activity.OperationName == "nestedset.move_to");
        Assert.Equal(
            1,
            Assert.Single(capture.Measurements, item => item.Name == "nestedset.operation.count").Value);
        Assert.True(
            Assert.Single(capture.Measurements, item => item.Name == "nestedset.rows.affected").Value > 0);
    }

    /// <summary>An unrelated native EF save does not claim a hierarchy operation occurred.</summary>
    [Fact]
    public async Task UnrelatedNativeSaveDoesNotEmitHierarchyMetrics()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        context.Add(new OrderingMarker { Id = 1 });
        using var capture = new DiagnosticsCapture();

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.Empty(capture.Activities);
        Assert.Empty(capture.Measurements);
    }

    /// <summary>A rejected lock write emits its bounded library error without recording identifiers.</summary>
    [Fact]
    public async Task UnacquiredWriteLockHasBoundedClassification()
    {
        // Arrange
        var database = await _relational.ResetAsync(Engine);
        var probe = new LockCommandFailureProbe();
        await using var context = database.CreateContext(probe);
        var service = context
            .NestedSet<TreeNode>()
            .ForScope(934857);

        using var capture = new DiagnosticsCapture();

        // Act
        var error = await Assert.ThrowsAsync<NestedSetException>(() =>
            service.InsertRootAsync(new TreeNode { NodeId = 842653 }, Guid.Empty, CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.LockAcquisitionFailed, error.Code);
        var operation = Assert.Single(
            capture.Activities,
            activity => activity.OperationName == "nestedset.insert_root");

        Assert.Equal(ActivityStatusCode.Error, operation.Status);
        Assert.Equal("lock_acquisition_failed", operation.GetTagItem("error.type"));
        Assert.Equal(
            1,
            Assert.Single(capture.Measurements, item => item.Name == "nestedset.operation.count").Value);
        Assert.Equal(
            1,
            Assert.Single(capture.Measurements, item => item.Name == "nestedset.operation.failures").Value);
        Assert.Single(capture.Measurements, item => item.Name == "nestedset.lock.wait.duration");
        AssertPrivate(capture, "insert_root", "lock", "success", "failure", "lock_acquisition_failed");
    }

    /// <summary>A provider error retains its instance while telemetry exposes only the database category.</summary>
    [Fact]
    public async Task ProviderLockFailureDoesNotExposeItsMessage()
    {
        // Arrange
        var database = await _relational.ResetAsync(Engine);
        var failure = new LockProviderFailureException();
        var probe = new LockCommandFailureProbe(failure);
        await using var context = database.CreateContext(probe);
        var service = context
            .NestedSet<TreeNode>()
            .ForScope(934857);

        using var capture = new DiagnosticsCapture();

        // Act
        var error = await Assert.ThrowsAsync<LockProviderFailureException>(() =>
            service.InsertRootAsync(new TreeNode { NodeId = 842653 }, Guid.Empty, CancellationToken.None));

        // Assert
        Assert.Same(failure, error);
        var operation = Assert.Single(
            capture.Activities,
            activity => activity.OperationName == "nestedset.insert_root");

        Assert.Equal(ActivityStatusCode.Error, operation.Status);
        Assert.Equal("database", operation.GetTagItem("error.type"));
        Assert.Equal(
            1,
            Assert.Single(capture.Measurements, item => item.Name == "nestedset.operation.count").Value);
        Assert.Equal(
            1,
            Assert.Single(capture.Measurements, item => item.Name == "nestedset.operation.failures").Value);
        Assert.Single(capture.Measurements, item => item.Name == "nestedset.lock.wait.duration");
        AssertPrivate(capture, "insert_root", "lock", "success", "failure", "database");
    }

    /// <summary>The target facade observes a forest import exactly once.</summary>
    [Fact]
    public async Task ForestImportIsObservedOnce()
    {
        // Arrange
        var database = await _relational.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        var treeId = Guid.Parse("20f79721-8dc6-4c04-b464-f989258c2874");
        var import = new NestedSetTreeImport<TreeNode, Guid>(
            treeId,
            new NestedSetBranch<TreeNode>(new TreeNode { NodeId = 1 }));

        using var capture = new DiagnosticsCapture();

        // Act
        await hierarchy.InsertForestAsync([import], CancellationToken.None);

        // Assert
        Assert.Single(capture.Activities, activity => activity.OperationName == "nestedset.insert_forest");
        AssertPrivate(capture, "insert_forest", "lock", "success");
    }

    /// <summary>The target facade observes a cross-tree move exactly once.</summary>
    [Fact]
    public async Task CrossTreeMoveIsObservedOnce()
    {
        // Arrange
        var database = await _relational.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        var sourceTree = Guid.Parse("f28b5de5-a11b-4da4-b48f-3be6cdb129db");
        var targetTree = Guid.Parse("e6a06c13-c7fd-496b-bec1-fc6d284358cc");
        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, sourceTree, CancellationToken.None);
        await hierarchy.InsertChildAsync(new TreeNode { NodeId = 2 }, 1, CancellationToken.None);
        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 3 }, targetTree, CancellationToken.None);
        using var capture = new DiagnosticsCapture();

        // Act
        await hierarchy.MoveToAsync(2, 3, CancellationToken.None);

        // Assert
        Assert.Single(capture.Activities, activity => activity.OperationName == "nestedset.move_to");
        AssertPrivate(capture, "move_to", "lock", "success");
    }

    /// <summary>The target facade observes detaching a subtree exactly once.</summary>
    [Fact]
    public async Task DetachAsTreeIsObservedOnce()
    {
        // Arrange
        var database = await _relational.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        var sourceTree = Guid.Parse("8e265248-8932-49dc-8aec-40cdb48f06da");
        var detachedTree = Guid.Parse("00adfbda-25fc-4752-af78-9cc9cc533f67");
        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, sourceTree, CancellationToken.None);
        await hierarchy.InsertChildAsync(new TreeNode { NodeId = 2 }, 1, CancellationToken.None);
        using var capture = new DiagnosticsCapture();

        // Act
        await hierarchy.DetachAsTreeAsync(2, detachedTree, CancellationToken.None);

        // Assert
        Assert.Single(capture.Activities, activity => activity.OperationName == "nestedset.detach_as_tree");
        AssertPrivate(capture, "detach_as_tree", "lock", "success");
    }

    /// <summary>The target facade observes identity-based whole-tree deletion exactly once.</summary>
    [Fact]
    public async Task DeleteTreeIsObservedOnce()
    {
        // Arrange
        var database = await _relational.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        var treeId = Guid.Parse("edc0049e-6f8e-460a-a3f9-a1ad52610104");
        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, treeId, CancellationToken.None);
        using var capture = new DiagnosticsCapture();

        // Act
        await hierarchy.DeleteTreeAsync(treeId, CancellationToken.None);

        // Assert
        Assert.Single(capture.Activities, activity => activity.OperationName == "nestedset.delete_tree");
        AssertPrivate(capture, "delete_tree", "lock", "success");
    }

    /// <summary>The administrative purge reports one bounded operation without exposing its TreeId.</summary>
    [Fact]
    public async Task PurgeTreeIdIsObservedOnce()
    {
        // Arrange
        var database = await _relational.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        var treeId = Guid.Parse("d5291350-2428-42f3-9205-0c8e55dd0125");
        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, treeId, CancellationToken.None);
        await hierarchy.DeleteTreeAsync(treeId, CancellationToken.None);
        using var capture = new DiagnosticsCapture();

        // Act
        await hierarchy.PurgeTreeIdAsync(treeId, CancellationToken.None);

        // Assert
        Assert.Single(capture.Activities, activity => activity.OperationName == "nestedset.purge_tree_id");
        AssertPrivate(capture, "purge_tree_id", "lock", "success");
    }

    /// <summary>An active-tree purge reports a stable bounded failure classification.</summary>
    [Fact]
    public async Task ActiveTreePurgeHasBoundedClassification()
    {
        // Arrange
        var database = await _relational.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        var treeId = Guid.Parse("5d9e4a55-9992-4b53-8fa5-11ef36306b6f");
        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, treeId, CancellationToken.None);
        using var capture = new DiagnosticsCapture();

        // Act
        var error = await Assert.ThrowsAsync<NestedSetException>(() =>
            hierarchy.PurgeTreeIdAsync(treeId, CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.TreeIdNotTombstoned, error.Code);
        var operation = Assert.Single(
            capture.Activities,
            activity => activity.OperationName == "nestedset.purge_tree_id");

        Assert.Equal("tree_id_not_tombstoned", operation.GetTagItem("error.type"));
        AssertPrivate(capture, "purge_tree_id", "lock", "success", "failure", "tree_id_not_tombstoned");
    }

    /// <summary>Checks every emitted tag and forbids exception payloads on all recorded activities.</summary>
    /// <param name="capture">The real listeners whose observations are checked.</param>
    /// <param name="allowedValues">The complete bounded vocabulary expected for this operation.</param>
    private static void AssertPrivate(
        DiagnosticsCapture capture,
        params string[] allowedValues
    )
    {
        var allowedKeys = new[]
        {
            "nestedset.operation", "nestedset.outcome", "nestedset.provider", "error.type",
        };

        var providerFamilies = new[]
        {
            "sqlite",
            "mysql_mariadb",
            "postgresql",
            "sql_server",
            "unknown",
        };

        foreach (var activity in capture.Activities)
        {
            Assert.Empty(activity.Events);
            Assert.Null(activity.StatusDescription);

            foreach (var tag in activity.TagObjects)
            {
                Assert.Contains(tag.Key, allowedKeys);
                var value = Assert.IsType<string>(tag.Value);

                Assert.Contains(value, tag.Key == "nestedset.provider" ? providerFamilies : allowedValues);
            }
        }

        foreach (var measurement in capture.Measurements)
        {
            foreach (var tag in measurement.Tags)
            {
                Assert.Contains(tag.Key, allowedKeys);
                var value = Assert.IsType<string>(tag.Value);

                Assert.Contains(value, tag.Key == "nestedset.provider" ? providerFamilies : allowedValues);
            }
        }
    }

    /// <summary>Maps test-engine variants to the package's bounded provider families.</summary>
    private static string ProviderFamily(
        string engine
    ) => engine switch
    {
        "Sqlite" => "sqlite",
        "MySql" or "MariaDb" => "mysql_mariadb",
        "PostgreSql" => "postgresql",
        "SqlServer" => "sql_server",
        _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, null),
    };
}

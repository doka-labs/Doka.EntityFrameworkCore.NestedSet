namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks tracing, metrics, and privacy for operations and coordinated saves.</summary>
public abstract partial class DiagnosticsTests : ProviderTest
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
    public async Task InsertRootAsync_SuccessfulMutation_MeasuresOperationAndLockWithoutPayloadTags()
    {
        // Arrange
        var database = await _relational.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
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
        await service.InsertRootAsync(node, Guid.Empty, TestContext.Current.CancellationToken);

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
            Assert.Single(capture.Measurements, item => item.Name == "nestedset.operation.count")
                .Value);

        Assert.True(
            Assert.Single(capture.Measurements, item => item.Name == "nestedset.operation.duration")
                .Value
            > 0);

        Assert.True(
            Assert.Single(capture.Measurements, item => item.Name == "nestedset.lock.wait.duration")
                .Value
            > 0);

        Assert.Equal(
            1,
            Assert.Single(capture.Measurements, item => item.Name == "nestedset.rows.affected")
                .Value);

        Assert.DoesNotContain(capture.Measurements, item => item.Name == "nestedset.operation.failures");
        AssertPrivate(capture, "insert_root", "lock", "success");
    }

    /// <summary>Rebuild emits exact node, batch, and affected-row measurements without identity tags.</summary>
    [Fact]
    public async Task RebuildAsync_Rebuild_PublishesBoundedWorkMeasurements()
    {
        // Arrange
        var database = await _relational.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var treeId = Guid.Parse("8f1f3065-baca-4a03-a179-a6d6d09cf935");
        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, treeId, TestContext.Current.CancellationToken);
        await hierarchy.InsertChildAsync(new TreeNode { NodeId = 2 }, 1, TestContext.Current.CancellationToken);
        _ = await context
            .Set<TreeNode>()
            .Where(node => node.NodeId == 2)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(node => node.Depth, 7),
                TestContext.Current.CancellationToken);

        using var capture = new DiagnosticsCapture();

        // Act
        await hierarchy
            .InTree(treeId)
            .RebuildAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            2,
            Assert.Single(capture.Measurements, item => item.Name == "nestedset.rebuild.node.count")
                .Value);

        Assert.Equal(
            1,
            Assert.Single(capture.Measurements, item => item.Name == "nestedset.batch.count")
                .Value);

        Assert.Equal(
            1,
            Assert.Single(capture.Measurements, item => item.Name == "nestedset.rows.affected")
                .Value);

        AssertPrivate(capture, "rebuild", "lock", "success");
    }

    /// <summary>The target facade observes a forest import exactly once.</summary>
    [Fact]
    public async Task InsertForestAsync_ForestImport_IsObservedOnce()
    {
        // Arrange
        var database = await _relational.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
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
        await hierarchy.InsertForestAsync([import], TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(capture.Activities, activity => activity.OperationName == "nestedset.insert_forest");
        AssertPrivate(capture, "insert_forest", "lock", "success");
    }

    /// <summary>The target facade observes a cross-tree move exactly once.</summary>
    [Fact]
    public async Task MoveToAsync_CrossTreeMove_IsObservedOnce()
    {
        // Arrange
        var database = await _relational.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        var sourceTree = Guid.Parse("f28b5de5-a11b-4da4-b48f-3be6cdb129db");
        var targetTree = Guid.Parse("e6a06c13-c7fd-496b-bec1-fc6d284358cc");
        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, sourceTree, TestContext.Current.CancellationToken);
        await hierarchy.InsertChildAsync(new TreeNode { NodeId = 2 }, 1, TestContext.Current.CancellationToken);
        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 3 }, targetTree, TestContext.Current.CancellationToken);
        using var capture = new DiagnosticsCapture();

        // Act
        await hierarchy.MoveToAsync(2, 3, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(capture.Activities, activity => activity.OperationName == "nestedset.move_to");
        AssertPrivate(capture, "move_to", "lock", "success");
    }

    /// <summary>The target facade observes detaching a subtree exactly once.</summary>
    [Fact]
    public async Task DetachAsTreeAsync_DetachAsTree_IsObservedOnce()
    {
        // Arrange
        var database = await _relational.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        var sourceTree = Guid.Parse("8e265248-8932-49dc-8aec-40cdb48f06da");
        var detachedTree = Guid.Parse("00adfbda-25fc-4752-af78-9cc9cc533f67");
        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, sourceTree, TestContext.Current.CancellationToken);
        await hierarchy.InsertChildAsync(new TreeNode { NodeId = 2 }, 1, TestContext.Current.CancellationToken);
        using var capture = new DiagnosticsCapture();

        // Act
        await hierarchy.DetachAsTreeAsync(2, detachedTree, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(capture.Activities, activity => activity.OperationName == "nestedset.detach_as_tree");
        AssertPrivate(capture, "detach_as_tree", "lock", "success");
    }

    /// <summary>The target facade observes identity-based whole-tree deletion exactly once.</summary>
    [Fact]
    public async Task DeleteTreeAsync_DeleteTree_IsObservedOnce()
    {
        // Arrange
        var database = await _relational.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        var treeId = Guid.Parse("edc0049e-6f8e-460a-a3f9-a1ad52610104");
        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, treeId, TestContext.Current.CancellationToken);
        using var capture = new DiagnosticsCapture();

        // Act
        await hierarchy.DeleteTreeAsync(treeId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(capture.Activities, activity => activity.OperationName == "nestedset.delete_tree");
        AssertPrivate(capture, "delete_tree", "lock", "success");
    }

    /// <summary>The administrative purge reports one bounded operation without exposing its TreeId.</summary>
    [Fact]
    public async Task PurgeTreeIdAsync_PurgeTreeId_IsObservedOnce()
    {
        // Arrange
        var database = await _relational.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        var treeId = Guid.Parse("d5291350-2428-42f3-9205-0c8e55dd0125");
        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, treeId, TestContext.Current.CancellationToken);
        await hierarchy.DeleteTreeAsync(treeId, TestContext.Current.CancellationToken);
        using var capture = new DiagnosticsCapture();

        // Act
        await hierarchy.PurgeTreeIdAsync(treeId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(capture.Activities, activity => activity.OperationName == "nestedset.purge_tree_id");
        AssertPrivate(capture, "purge_tree_id", "lock", "success");
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

                Assert.Contains(
                    value,
                    tag.Key == "nestedset.provider"
                        ? providerFamilies
                        : allowedValues);
            }
        }

        foreach (var measurement in capture.Measurements)
        {
            foreach (var tag in measurement.Tags)
            {
                Assert.Contains(tag.Key, allowedKeys);
                var value = Assert.IsType<string>(tag.Value);

                Assert.Contains(
                    value,
                    tag.Key == "nestedset.provider"
                        ? providerFamilies
                        : allowedValues);
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

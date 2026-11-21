namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Checks single-insert tracker cost with many unrelated clean application entries.</summary>
[Collection("Scale scenarios")]
public sealed class SingleInsertScaleTests : ProviderTest,
    IClassFixture<ProviderFixture<RelationalFixture, SqliteEngine>>
{
    private readonly RelationalFixture _fixture;

    /// <summary>Uses the qualified SQLite fixture and checks isolated tracker state.</summary>
    /// <param name="fixture">The SQLite fixture owning the relational database resource.</param>
    public SingleInsertScaleTests(
        ProviderFixture<RelationalFixture, SqliteEngine> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Inserting one child avoids repeated global detection and unrelated entry wrappers.</summary>
    [Fact]
    public async Task InsertChildAsync_UnrelatedTrackedEntities_PreserveDetectionAndState()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var baseline = database.CreateContext();
        var baselineHierarchy = baseline
            .NestedSet<TreeNode>()
            .ForScope(7);

        await baselineHierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, Guid.NewGuid(), CancellationToken.None);

        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 3 }, Guid.NewGuid(), CancellationToken.None);

        var markers = Enumerable
            .Range(1, 20_000)
            .Select(id => new UnrelatedRow { Id = id })
            .ToArray();

        context.AttachRange(markers);
        var detections = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detections++;

        // Act
        await baselineHierarchy.InsertChildAsync(new TreeNode { NodeId = 2 }, 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(new TreeNode { NodeId = 4 }, 3, CancellationToken.None);

        // Assert
        Assert.Equal(2, detections);

        Assert.Equal(
            20_000,
            context
                .ChangeTracker
                .Entries<UnrelatedRow>()
                .Count(marker => marker.State == EntityState.Unchanged));

        Assert.Equal(
            4,
            await context
                .Set<TreeNode>()
                .CountAsync(CancellationToken.None));
    }
}

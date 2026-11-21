namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Bounds unrelated tracker work across globally batched imports of independent trees.</summary>
[Collection("Scale scenarios")]
public sealed class BulkForestTrackerScaleTests : ProviderTest,
    IClassFixture<ProviderFixture<RelationalFixture, SqliteEngine>>
{
    private const int RootCount = 131;
    private const int TrackedCount = 20_000;
    private readonly RelationalFixture _fixture;

    /// <summary>Uses the qualified SQLite fixture and checks the tracker work boundary.</summary>
    /// <param name="fixture">The SQLite fixture owning the relational database resource.</param>
    public BulkForestTrackerScaleTests(
        ProviderFixture<RelationalFixture, SqliteEngine> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Independent trees share three payload waves without multiplying full tracker scans.</summary>
    [Fact]
    public async Task InsertForestAsync_IndependentForest_KeepsGlobalTrackerWorkBounded()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var baseline = database.CreateContext();
        var baselineHierarchy = baseline
            .NestedSet<TreeNode>()
            .ForScope(7);

        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        await baselineHierarchy.InsertForestAsync(
            [
                new NestedSetTreeImport<TreeNode, Guid>(
                    Guid.NewGuid(),
                    new NestedSetBranch<TreeNode>(new TreeNode { NodeId = 1_000_001 })),
            ],
            CancellationToken.None);

        await hierarchy.InsertForestAsync(
            [
                new NestedSetTreeImport<TreeNode, Guid>(
                    Guid.NewGuid(),
                    new NestedSetBranch<TreeNode>(new TreeNode { NodeId = 1_000_002 })),
            ],
            CancellationToken.None);

        var baselineTrees = CreateTrees(1);
        var trees = CreateTrees(RootCount + 1);
        var markers = Enumerable
            .Range(1, TrackedCount)
            .Select(id => new UnrelatedRow { Id = id })
            .ToArray();

        context.AttachRange(markers);
        var detections = 0;
        var saves = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detections++;
        context.SavedChanges += (_, _) => saves++;

        // Act
        await baselineHierarchy.InsertForestAsync(baselineTrees, CancellationToken.None);
        await hierarchy.InsertForestAsync(trees, CancellationToken.None);

        // Assert
        Assert.Equal(3, saves);
        Assert.Equal(4, detections);

        Assert.All(
            trees,
            tree =>
            {
                Assert.Equal(tree.TreeId, tree.Root.Entity.TreeId);
                Assert.Equal(
                    (1L, 2L, 0, 0L),
                    (tree.Root.Entity.Start, tree.Root.Entity.End, tree.Root.Entity.Depth, tree.Root.Entity.Position));

                Assert.Null(tree.Root.Entity.Parent);
                Assert.Equal(
                    EntityState.Detached,
                    context.Entry(tree.Root.Entity)
                        .State);
            });

        Assert.Equal(
            TrackedCount,
            context
                .ChangeTracker
                .Entries<UnrelatedRow>()
                .Count(marker => marker.State == EntityState.Unchanged));

        Assert.Equal(
            RootCount * 2 + 2,
            await context
                .Set<TreeNode>()
                .CountAsync(CancellationToken.None));
    }

    /// <summary>Builds identical assigned-key forest workloads outside the measured allocation interval.</summary>
    private static NestedSetTreeImport<TreeNode, Guid>[] CreateTrees(
        int firstKey
    ) => Enumerable
        .Range(firstKey, RootCount)
        .Select(id => new NestedSetTreeImport<TreeNode, Guid>(
            Guid.NewGuid(),
            new NestedSetBranch<TreeNode>(new TreeNode { NodeId = id })))
        .ToArray();
}

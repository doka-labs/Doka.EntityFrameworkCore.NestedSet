namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Bounds unrelated tracker work across globally batched imports of independent trees.</summary>
[Collection("Allocation measurements")]
public sealed class BulkForestTrackerScaleTests : ProviderTest,
    IClassFixture<ProviderFixture<RelationalFixture, SqliteEngine>>
{
    private const int RootCount = 131;
    private const int TrackedCount = 20_000;
    private readonly RelationalFixture _fixture;
    private readonly ITestOutputHelper _output;

    /// <summary>Uses the qualified SQLite fixture and records the measured tracker allocation delta.</summary>
    /// <param name="fixture">The SQLite fixture owning the relational database resource.</param>
    /// <param name="output">The sink recording measured allocation evidence.</param>
    public BulkForestTrackerScaleTests(
        ProviderFixture<RelationalFixture, SqliteEngine> fixture,
        ITestOutputHelper output
    ) : base(fixture)
    {
        _fixture = fixture.Value;
        _output = output;
    }

    /// <summary>Independent trees share three payload waves without multiplying full tracker scans.</summary>
    [Fact]
    public async Task IndependentForestKeepsGlobalTrackerWorkBounded()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
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
        var baselineBefore = GC.GetTotalAllocatedBytes(precise: true);
        await baselineHierarchy.InsertForestAsync(baselineTrees, CancellationToken.None);
        var baselineAllocated = GC.GetTotalAllocatedBytes(precise: true) - baselineBefore;
        var crowdedBefore = GC.GetTotalAllocatedBytes(precise: true);
        await hierarchy.InsertForestAsync(trees, CancellationToken.None);
        var crowdedAllocated = GC.GetTotalAllocatedBytes(precise: true) - crowdedBefore;
        var additionalPerEntry = (crowdedAllocated - baselineAllocated) / markers.Length;

        // Assert
        _output.WriteLine(
            $"Independent roots={RootCount}; unrelated tracked entities={TrackedCount}; "
            + $"detections={detections}; saves={saves}; baseline bytes={baselineAllocated}; "
            + $"crowded bytes={crowdedAllocated}; additional bytes per entry={additionalPerEntry}");
        Assert.Equal(3, saves);
        Assert.Equal(4, detections);
        // WHY: Four mandatory detections comprise one executor guard and three EF payload saves. The existing
        // single-insert ceiling allows 500 bytes per entry for two detections, so this bounds the same per-scan
        // cost without permitting additional scans or tracker snapshots for each independently imported tree.
        Assert.InRange(additionalPerEntry, 0, 1_000);
        Assert.All(
            trees,
            tree =>
            {
                Assert.Equal(tree.TreeId, tree.Root.Entity.TreeId);
                Assert.Equal(
                    (1L, 2L, 0, 0L),
                    (tree.Root.Entity.Start, tree.Root.Entity.End, tree.Root.Entity.Depth, tree.Root.Entity.Position));
                Assert.Null(tree.Root.Entity.Parent);
                Assert.Equal(EntityState.Detached, context.Entry(tree.Root.Entity).State);
            });

        Assert.Equal(
            TrackedCount,
            context
                .ChangeTracker
                .Entries<UnrelatedRow>()
                .Count(marker => marker.State == EntityState.Unchanged));
        Assert.Equal(
            (RootCount * 2) + 2,
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

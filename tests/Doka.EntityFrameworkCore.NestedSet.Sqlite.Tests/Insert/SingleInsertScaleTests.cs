namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Checks single-insert tracker cost with many unrelated clean application entries.</summary>
[Collection("Allocation measurements")]
public sealed class SingleInsertScaleTests : ProviderTest,
    IClassFixture<ProviderFixture<RelationalFixture, SqliteEngine>>
{
    private readonly RelationalFixture _fixture;
    private readonly ITestOutputHelper _output;

    /// <summary>Uses the qualified SQLite fixture and captures allocation evidence in test output.</summary>
    /// <param name="fixture">The SQLite fixture owning the relational database resource.</param>
    /// <param name="output">The sink recording measured allocation evidence.</param>
    public SingleInsertScaleTests(
        ProviderFixture<RelationalFixture, SqliteEngine> fixture,
        ITestOutputHelper output
    ) : base(fixture)
    {
        _fixture = fixture.Value;
        _output = output;
    }

    /// <summary>Inserting one child avoids repeated global detection and unrelated entry wrappers.</summary>
    [Fact]
    public async Task SingleInsertAllocationPerUnrelatedTrackedEntityStaysBounded()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
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
        var baselineBefore = GC.GetTotalAllocatedBytes(precise: true);
        await baselineHierarchy.InsertChildAsync(new TreeNode { NodeId = 2 }, 1, CancellationToken.None);
        var baselineAllocated = GC.GetTotalAllocatedBytes(precise: true) - baselineBefore;
        var crowdedBefore = GC.GetTotalAllocatedBytes(precise: true);
        await hierarchy.InsertChildAsync(new TreeNode { NodeId = 4 }, 3, CancellationToken.None);
        var crowdedAllocated = GC.GetTotalAllocatedBytes(precise: true) - crowdedBefore;
        var additionalPerEntry = (crowdedAllocated - baselineAllocated) / markers.Length;

        // Assert
        _output.WriteLine(
            $"Unrelated tracked entities=20000; detections={detections}; "
            + $"baseline bytes={baselineAllocated}; crowded bytes={crowdedAllocated}; "
            + $"additional bytes per entry={additionalPerEntry}");
        Assert.Equal(2, detections);
        // WHY: Two isolated .NET 10.0.12 SQLite runs measured 419 bytes per entry with the persistence guard.
        // A 500-byte ceiling allows runtime noise while rejecting substantial tracker-wide allocation growth.
        Assert.InRange(additionalPerEntry, 0, 500);
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

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Rejects edits after EF detection at the persistence boundary, including a zero-write save.</summary>
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, false, false, true)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, true, true)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, false, true)]
    [InlineData(false, true, true, false)]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, false)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, true, true)]
    public async Task LateDetectionCallbackCannotChangeHierarchy(
        bool wrapped,
        bool synchronous,
        bool pendingWrite,
        bool automaticDetection
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedAsync(setup, cancellationToken: TestContext.Current.CancellationToken);
        var probe = new SaveBoundaryProbe();
        await using var context = CreateManualDetectionContext(setup, wrapped, probe);
        var node = await context.Set<OrderingNode>().SingleAsync(value => value.Id == 1, CancellationToken.None);
        var originalLeft = node.Left;
        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        var detections = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detections++;

        if (pendingWrite)
        {
            await context.AddAsync(new OrderingMarker { Id = 1 }, CancellationToken.None);
        }

        var invoked = false;
        context.SavingChanges += (_, _) =>
        {
            // WHY: Attach after wrapper planning so this callback runs only after EF's native save detection.
            context.ChangeTracker.DetectedAllChanges += (_, _) =>
            {
                invoked = true;
                node.Left++;
            };

            if (!automaticDetection)
            {
                // WHY: Manual mode has no implicit scan; this application callback explicitly requests one.
                context.ChangeTracker.DetectChanges();
            }
        };

        // Act
        var failure = await Record.ExceptionAsync(() => SaveNavigationAsync(context, synchronous,
            cancellationToken: TestContext.Current.CancellationToken));

        var storedLeft = await setup.Set<OrderingNode>().Where(value => value.Id == 1)
            .Select(value => value.Left).SingleAsync(CancellationToken.None);

        var storedMarkers = await setup.Set<OrderingMarker>().CountAsync(CancellationToken.None);

        // Assert
        Assert.True(invoked);
        Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(failure).Code);
        Assert.Equal(0, probe.PayloadCommands);
        Assert.Equal(originalLeft, storedLeft);
        Assert.Equal(0, storedMarkers);
        var expectedDetections = automaticDetection ? (wrapped && !synchronous ? 2 : 1) : 1;
        Assert.Equal(expectedDetections, detections);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }
}

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>
    ///     Preserves native detection for ordinary and hierarchy-owned payload in every save entry point.
    /// </summary>
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
    public async Task SaveChanges_OrdinaryPayloadChanges_HonorAutomaticDetection(
        bool wrapped,
        bool synchronous,
        bool nodePayload,
        bool automaticDetection
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        await SeedAsync(setup, cancellationToken: TestContext.Current.CancellationToken);
        await setup.AddAsync(
            new OrderingMarker
            {
                Id = 1,
                Value = "original",
            },
            CancellationToken.None);

        await setup.SaveChangesAsync(CancellationToken.None);
        var probe = new SaveBoundaryProbe();
        await using var context = CreateManualDetectionContext(setup, wrapped, probe);
        var marker = await context
            .Set<OrderingMarker>()
            .SingleAsync(CancellationToken.None);

        var node = await context
            .Set<OrderingNode>()
            .SingleAsync(value => value.Id == 1, CancellationToken.None);

        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        var detections = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detections++;

        if (nodePayload)
        {
            node.Payload = "ordinary change";
        }
        else
        {
            marker.Value = "ordinary change";
        }

        // Act
        // WHY: The synchronous variants verify the explicitly synchronous public save boundary.
        // ReSharper disable once MethodHasAsyncOverload
        var saved = synchronous
            ? context.SaveChanges()
            : await context.SaveChangesAsync(CancellationToken.None);

        var stored = nodePayload
            ? await setup
                .Set<OrderingNode>()
                .Where(value => value.Id == 1)
                .Select(value => value.Payload)
                .SingleAsync(CancellationToken.None)
            : await setup
                .Set<OrderingMarker>()
                .Select(value => value.Value)
                .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(
            automaticDetection
                ? "ordinary change"
                : "original",
            stored);

        Assert.Equal(
            automaticDetection
                ? 1
                : 0,
            saved);

        var expectedDetections = automaticDetection
            ? wrapped && !synchronous
                ? 2
                : 1
            : 0;

        Assert.Equal(expectedDetections, detections);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(0, probe.LockCommands);
    }

    /// <summary>Saves an explicitly marked ordinary value without detecting unmarked hierarchy payload.</summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task SaveChanges_ExplicitPayload_MarksPreserveOtherUndetectedChanges(
        bool wrapped,
        bool synchronous,
        bool callbackWrite
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        await SeedAsync(setup, cancellationToken: TestContext.Current.CancellationToken);
        await setup.AddAsync(
            new OrderingMarker
            {
                Id = 1,
                Value = "original",
            },
            CancellationToken.None);

        await setup.SaveChangesAsync(CancellationToken.None);
        var probe = new SaveBoundaryProbe();
        var callback = new SaveBoundaryCallback();
        await using var context = CreateManualDetectionContext(setup, wrapped, probe, callback);
        var marker = await context
            .Set<OrderingMarker>()
            .SingleAsync(CancellationToken.None);

        var node = await context
            .Set<OrderingNode>()
            .SingleAsync(value => value.Id == 1, CancellationToken.None);

        context.ChangeTracker.AutoDetectChangesEnabled = false;
        var markerValue = context
            .Entry(marker)
            .Property(value => value.Value);

        node.Payload = "undetected payload";
        var detections = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detections++;
        var markPayload = () =>
        {
            markerValue.CurrentValue = "explicitly marked";
            markerValue.IsModified = true;
        };

        if (callbackWrite)
        {
            callback.OnSaving = _ => markPayload();
        }
        else
        {
            markPayload();
        }

        // Act
        // WHY: The synchronous variants verify the explicitly synchronous public save boundary.
        // ReSharper disable once MethodHasAsyncOverload
        var saved = synchronous
            ? context.SaveChanges()
            : await context.SaveChangesAsync(CancellationToken.None);

        var storedMarker = await setup
            .Set<OrderingMarker>()
            .Select(value => value.Value)
            .SingleAsync(CancellationToken.None);

        var storedPayload = await setup
            .Set<OrderingNode>()
            .Where(value => value.Id == 1)
            .Select(value => value.Payload)
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, saved);
        Assert.Equal("explicitly marked", storedMarker);
        Assert.Equal("original", storedPayload);
        Assert.Equal(0, detections);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(1, probe.PayloadCommands);
        Assert.Equal(0, probe.LockCommands);
    }

    /// <summary>
    ///     Coordinates an explicitly marked parent edit without overriding manual change detection.
    /// </summary>
    [Fact]
    public async Task SaveChangesAsync_ManualDetection_CoordinatesExplicitParentChange()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        await SeedAsync(setup, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = CreateManualDetectionContext(setup, wrapped: true);
        var node = await context
            .Set<OrderingNode>()
            .SingleAsync(value => value.Id == 1, CancellationToken.None);

        context.ChangeTracker.AutoDetectChangesEnabled = false;
        node.ParentId = 2;
        context
            .Entry(node)
            .Property(value => value.ParentId)
            .IsModified = true;

        // Act
        await context.SaveChangesAsync(CancellationToken.None);
        var stored = await setup
            .Set<OrderingNode>()
            .Where(value => value.Id == 1)
            .Select(value => new
            {
                value.ParentId,
                value.Depth,
                value.Left,
                value.Right,
            })
            .SingleAsync(CancellationToken.None);

        var parent = await setup
            .Set<OrderingNode>()
            .Where(value => value.Id == 2)
            .Select(value => new
            {
                value.Depth,
                value.Left,
                value.Right,
            })
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, stored.ParentId);
        Assert.Equal(parent.Depth + 1, stored.Depth);
        Assert.True(parent.Left < stored.Left);
        Assert.True(stored.Right < parent.Right);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Reuses the same mapped provider graph while selecting the coordinated or ordinary save API.</summary>
    /// <param name="setup">The initialized fixture whose tables and connection will be shared.</param>
    /// <param name="wrapped">Whether to use the public coordinated save overrides.</param>
    /// <param name="interceptors">Observers owned by the current test.</param>
    /// <returns>A context owned by the calling test.</returns>
    private static DbContext CreateManualDetectionContext(
        DbContext setup,
        bool wrapped,
        params IInterceptor[] interceptors
    )
    {
        var options = SaveBoundaryTestSupport.Options(setup, interceptors)
            .Options;

        if (wrapped)
        {
            return new SaveBoundaryContext(options);
        }

        // WHY: A DbContextOptions<SaveBoundaryContext> cannot construct an unrelated context type.
        // Retain its exact extensions through the existing context-neutral options shape instead.
        var extensions = options.Extensions.ToDictionary(extension => extension.GetType());

        return new UnwrappedSynchronousContext(new DbContextOptions<DbContext>(extensions));
    }
}

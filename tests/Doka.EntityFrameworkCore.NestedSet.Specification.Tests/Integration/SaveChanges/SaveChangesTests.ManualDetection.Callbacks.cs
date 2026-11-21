namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Preserves manual detection when a callback only edits an ordinary tracked value.</summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task SaveChanges_ManualCallbackPayload_RemainsUndetected(
        bool wrapped,
        bool synchronous,
        bool interceptor
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

        context.ChangeTracker.AutoDetectChangesEnabled = false;
        var detections = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detections++;

        if (interceptor)
        {
            callback.OnSaving = _ => marker.Value = "callback change";
        }
        else
        {
            context.SavingChanges += (_, _) => marker.Value = "callback change";
        }

        // Act
        // WHY: The synchronous variants verify the explicitly synchronous public save boundary.
        // ReSharper disable once MethodHasAsyncOverload
        var saved = synchronous
            ? context.SaveChanges()
            : await context.SaveChangesAsync(CancellationToken.None);

        var stored = await setup
            .Set<OrderingMarker>()
            .Select(value => value.Value)
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(0, saved);
        Assert.Equal("original", stored);
        Assert.Equal("callback change", marker.Value);
        Assert.Equal(0, detections);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(0, probe.PayloadCommands);
        Assert.Equal(0, probe.LockCommands);
    }

    /// <summary>
    ///     Honors consumer callback detection choices while preserving the library's original caller setting.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SaveChangesAsync_CoordinatedCallback_CanChooseNativeDetectionPolicy(
        bool interceptor,
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
        var callback = new SaveBoundaryCallback();
        await using var context = CreateManualDetectionContext(setup, wrapped: true, callback);
        var node = await context
            .Set<OrderingNode>()
            .SingleAsync(value => value.Id == 1, CancellationToken.None);

        var marker = await context
            .Set<OrderingMarker>()
            .SingleAsync(CancellationToken.None);

        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        context
            .Entry(node)
            .Property(value => value.Name)
            .CurrentValue = "Zulu";

        bool? observedPolicy = null;
        var detections = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detections++;
        var choosePolicy = () =>
        {
            observedPolicy = context.ChangeTracker.AutoDetectChangesEnabled;
            context.ChangeTracker.AutoDetectChangesEnabled = !automaticDetection;
            marker.Value = "callback payload";
            node.Payload = "callback payload";
        };

        if (interceptor)
        {
            callback.OnSaving = _ => choosePolicy();
        }
        else
        {
            context.SavingChanges += (_, _) => choosePolicy();
        }

        // Act
        await context.SaveChangesAsync(CancellationToken.None);
        var stored = await setup
            .Set<OrderingNode>()
            .Where(value => value.Id == 1)
            .Select(value => new
            {
                value.Name,
                value.Payload,
            })
            .SingleAsync(CancellationToken.None);

        var storedMarker = await setup
            .Set<OrderingMarker>()
            .Select(value => value.Value)
            .SingleAsync(CancellationToken.None);

        // Assert
        var expectedPayload = automaticDetection
            ? "original"
            : "callback payload";

        Assert.Equal("Zulu", stored.Name);
        Assert.Equal(expectedPayload, stored.Payload);
        Assert.Equal(expectedPayload, storedMarker);
        Assert.Equal(automaticDetection, observedPolicy);
        Assert.Equal(1, detections);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }
}

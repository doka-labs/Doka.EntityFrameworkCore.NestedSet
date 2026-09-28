namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>
    ///     Rejects a first hierarchy change introduced by either callback surface before any payload SQL.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task CallbackCannotIntroduceUnlockedOrderingChanges(
        bool interceptor,
        bool disableAutomaticDetection
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        await SeedAsync(setup);
        var probe = new SaveBoundaryProbe();
        var callback = new SaveBoundaryCallback();
        await using var context = new SaveBoundaryContext(
            SaveBoundaryTestSupport.Options(setup, probe, callback)
                .Options);

        var node = await context
            .Set<OrderingNode>()
            .SingleAsync(value => value.Id == 1, CancellationToken.None);

        node.Payload = "pending payload";
        context.ChangeTracker.DetectChanges();
        context.ChangeTracker.AutoDetectChangesEnabled = !disableAutomaticDetection;

        if (interceptor)
        {
            // WHY: The entry is already Modified, so StateChanged alone cannot guard the additional sort property.
            callback.OnSaving = _ => context
                .Entry(node)
                .Property(value => value.Name)
                .CurrentValue = "Zulu";
        }
        else
        {
            context.SavingChanges += (_, _) => node.Name = "Zulu";
        }

        // Act
        var failure = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));

        // Assert
        var rejection = Assert.IsType<NestedSetException>(failure);
        Assert.Equal(NestedSetErrorCode.InvalidContext, rejection.Code);
        Assert.Contains("Save callbacks", rejection.Message, StringComparison.Ordinal);
        Assert.Equal(0, probe.PayloadCommands);
        Assert.Equal(0, probe.LockCommands);
        Assert.Equal(!disableAutomaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal("Zulu", node.Name);
        Assert.Equal(
            "Alpha",
            await setup
                .Set<OrderingNode>()
                .Where(value => value.Id == 1)
                .Select(value => value.Name)
                .SingleAsync(CancellationToken.None));
    }

    /// <summary>Rejects callback changes to an already planned entity, including the same trigger property.</summary>
    /// <param name="sameProperty">Whether the callback replaces the planned value or adds another trigger.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallbackCannotAlterPlannedOrderingChange(
        bool sameProperty
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        await SeedAsync(setup);
        var probe = new SaveBoundaryProbe();
        var callback = new SaveBoundaryCallback();
        await using var context = new SaveBoundaryContext(
            SaveBoundaryTestSupport.Options(setup, probe, callback).Options);

        var node = await context
            .Set<OrderingNode>()
            .SingleAsync(value => value.Id == 1, CancellationToken.None);

        node.Name = "Zulu";
        callback.OnSaving = _ =>
        {
            if (sameProperty)
            {
                node.Name = "Yankee";
            }
            else
            {
                node.Priority = 42;
            }
        };

        // Act
        var failure = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));

        // Assert
        var rejection = Assert.IsType<NestedSetException>(failure);
        Assert.Equal(NestedSetErrorCode.InvalidContext, rejection.Code);
        Assert.Contains("Save callbacks", rejection.Message, StringComparison.Ordinal);
        Assert.Equal(0, probe.PayloadCommands);
        Assert.Equal(
            "Alpha",
            await setup
                .Set<OrderingNode>()
                .Where(value => value.Id == 1)
                .Select(value => value.Name)
                .SingleAsync(CancellationToken.None));
    }

    /// <summary>
    ///     Runs only the necessary planning and post-callback scans, including after a previous lease reset.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SaveRunsOnePlanningAndOneCallbackDetectionPass(
        bool orderedChange,
        bool automaticDetection
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        await SeedAsync(setup);
        await using var context = new SaveBoundaryContext(
            SaveBoundaryTestSupport.Options(setup).Options);

        var node = await context
            .Set<OrderingNode>()
            .SingleAsync(value => value.Id == 1, CancellationToken.None);

        node.Payload = "changed";

        if (orderedChange)
        {
            node.Name = "Zulu";
        }

        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        var detections = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detections++;

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, detections);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }
}

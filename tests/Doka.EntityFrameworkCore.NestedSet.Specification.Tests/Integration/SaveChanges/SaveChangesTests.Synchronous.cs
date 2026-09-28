namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>
    ///     Rejects synchronous callback-created hierarchy changes after callbacks and before payload SQL.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SynchronousSaveGuardsEventsAndInterceptors(
        bool interceptor,
        bool automaticDetection
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

        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;

        if (interceptor)
        {
            callback.OnSaving = _ => node.Name = "Zulu";
        }
        else
        {
            context.SavingChanges += (_, _) => node.Name = "Zulu";
        }

        // Act
        // ReSharper disable once MethodHasAsyncOverload
        // WHY: This regression exercises the explicitly synchronous EF callback and persistence pipeline.
        var failure = Record.Exception(() => context.SaveChanges());

        // Assert
        var rejection = Assert.IsType<NestedSetException>(failure);
        Assert.Equal(NestedSetErrorCode.InvalidContext, rejection.Code);
        Assert.Equal(0, probe.PayloadCommands);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            "Alpha",
            await setup
                .Set<OrderingNode>()
                .Where(value => value.Id == 1)
                .Select(value => value.Name)
                .SingleAsync(CancellationToken.None));
    }

    /// <summary>Allows explicitly synchronous unrelated writes with native acceptance and one detection pass.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SynchronousUnrelatedSaveHonorsAcceptance(
        bool acceptChanges,
        bool automaticDetection
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        var probe = new SaveBoundaryProbe();
        await using var context = new SaveBoundaryContext(SaveBoundaryTestSupport.Options(setup, probe).Options);
        var marker = new OrderingMarker
        {
            Id = 1,
            Value = "explicit synchronous save",
        };

        await context.AddAsync(marker, CancellationToken.None);
        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        var detections = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detections++;

        // Act
        // ReSharper disable once MethodHasAsyncOverload
        // WHY: The sync override must retain EF's explicit synchronous acceptance contract for ordinary writes.
        var saved = context.SaveChanges(acceptChanges);

        // Assert
        Assert.Equal(1, saved);
        Assert.Equal(1, detections);
        Assert.Equal(1, probe.PayloadCommands);
        Assert.Equal(0, probe.LockCommands);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(acceptChanges ? EntityState.Unchanged : EntityState.Added, context.Entry(marker).State);
        Assert.Equal(
            marker.Value,
            await setup
                .Set<OrderingMarker>()
                .Select(row => row.Value)
                .SingleAsync(CancellationToken.None));
    }

    /// <summary>Prevents an inherited custom-base synchronous API from bypassing the post-callback guard.</summary>
    [Fact]
    public async Task CustomBaseRequiresExplicitSynchronousWrapper()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        await SeedAsync(setup);
        var extensions = setup
            .GetService<IDbContextOptions>()
            .Extensions
            .ToDictionary(extension => extension.GetType());

        await using var context = new UnwrappedSynchronousContext(new DbContextOptions<DbContext>(extensions));
        var node = await context
            .Set<OrderingNode>()
            .SingleAsync(value => value.Id == 1, CancellationToken.None);

        context.SavingChanges += (_, _) => node.Name = "Zulu";

        // Act
        // ReSharper disable once MethodHasAsyncOverload
        // WHY: The negative control deliberately uses the inherited sync API instead of a guarded override.
        var failure = Record.Exception(() => context.SaveChanges());

        // Assert
        var rejection = Assert.IsType<NestedSetException>(failure);
        Assert.Equal(NestedSetErrorCode.InvalidContext, rejection.Code);
        Assert.Contains(
            nameof(NestedSetDbContextExtensions.SaveNestedSetChanges),
            rejection.Message,
            StringComparison.Ordinal);
        Assert.Equal("Zulu", node.Name);
        Assert.Equal(
            "Alpha",
            await setup
                .Set<OrderingNode>()
                .Where(value => value.Id == 1)
                .Select(value => value.Name)
                .SingleAsync(CancellationToken.None));
    }
}

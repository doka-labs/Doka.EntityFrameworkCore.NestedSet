namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Rejects callback-only hierarchy changes with or without an explicitly pending ordinary write.</summary>
    [Theory]
    [InlineData(false, false, false, nameof(OrderingNode.Name), false)]
    [InlineData(false, false, false, nameof(OrderingNode.Name), true)]
    [InlineData(false, false, true, nameof(OrderingNode.Name), false)]
    [InlineData(false, false, true, nameof(OrderingNode.Name), true)]
    [InlineData(false, true, false, nameof(OrderingNode.Name), false)]
    [InlineData(false, true, false, nameof(OrderingNode.Name), true)]
    [InlineData(false, true, true, nameof(OrderingNode.Name), false)]
    [InlineData(false, true, true, nameof(OrderingNode.Name), true)]
    [InlineData(true, false, false, nameof(OrderingNode.Name), false)]
    [InlineData(true, false, false, nameof(OrderingNode.Name), true)]
    [InlineData(true, false, true, nameof(OrderingNode.Name), false)]
    [InlineData(true, false, true, nameof(OrderingNode.Name), true)]
    [InlineData(true, true, false, nameof(OrderingNode.Name), false)]
    [InlineData(true, true, false, nameof(OrderingNode.Name), true)]
    [InlineData(true, true, true, nameof(OrderingNode.Name), false)]
    [InlineData(true, true, true, nameof(OrderingNode.Name), true)]
    [InlineData(false, false, false, nameof(OrderingNode.ParentId), false)]
    [InlineData(false, false, false, nameof(OrderingNode.ParentId), true)]
    [InlineData(false, false, true, nameof(OrderingNode.ParentId), false)]
    [InlineData(false, false, true, nameof(OrderingNode.ParentId), true)]
    [InlineData(false, true, false, nameof(OrderingNode.ParentId), false)]
    [InlineData(false, true, false, nameof(OrderingNode.ParentId), true)]
    [InlineData(false, true, true, nameof(OrderingNode.ParentId), false)]
    [InlineData(false, true, true, nameof(OrderingNode.ParentId), true)]
    [InlineData(true, false, false, nameof(OrderingNode.ParentId), false)]
    [InlineData(true, false, false, nameof(OrderingNode.ParentId), true)]
    [InlineData(true, false, true, nameof(OrderingNode.ParentId), false)]
    [InlineData(true, false, true, nameof(OrderingNode.ParentId), true)]
    [InlineData(true, true, false, nameof(OrderingNode.ParentId), false)]
    [InlineData(true, true, false, nameof(OrderingNode.ParentId), true)]
    [InlineData(true, true, true, nameof(OrderingNode.ParentId), false)]
    [InlineData(true, true, true, nameof(OrderingNode.ParentId), true)]
    [InlineData(false, false, false, nameof(OrderingNode.Left), false)]
    [InlineData(false, false, false, nameof(OrderingNode.Left), true)]
    [InlineData(false, false, true, nameof(OrderingNode.Left), false)]
    [InlineData(false, false, true, nameof(OrderingNode.Left), true)]
    [InlineData(false, true, false, nameof(OrderingNode.Left), false)]
    [InlineData(false, true, false, nameof(OrderingNode.Left), true)]
    [InlineData(false, true, true, nameof(OrderingNode.Left), false)]
    [InlineData(false, true, true, nameof(OrderingNode.Left), true)]
    [InlineData(true, false, false, nameof(OrderingNode.Left), false)]
    [InlineData(true, false, false, nameof(OrderingNode.Left), true)]
    [InlineData(true, false, true, nameof(OrderingNode.Left), false)]
    [InlineData(true, false, true, nameof(OrderingNode.Left), true)]
    [InlineData(true, true, false, nameof(OrderingNode.Left), false)]
    [InlineData(true, true, false, nameof(OrderingNode.Left), true)]
    [InlineData(true, true, true, nameof(OrderingNode.Left), false)]
    [InlineData(true, true, true, nameof(OrderingNode.Left), true)]
    public async Task SaveChanges_ManualSave_RejectsLateHierarchyChanges(
        bool wrapped,
        bool synchronous,
        bool interceptor,
        string property,
        bool pendingWrite
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

        // WHY: Exercise both the IDatabase persistence boundary and EF's zero-write early exit.
        if (pendingWrite)
        {
            var markerValue = context
                .Entry(marker)
                .Property(value => value.Value);

            markerValue.CurrentValue = "pending payload";
            markerValue.IsModified = true;
        }

        var originalLeft = node.Left;
        Action changeHierarchy = property switch
        {
            nameof(OrderingNode.Name) => () => node.Name = "Zulu",
            nameof(OrderingNode.ParentId) => () => node.ParentId = 2,
            nameof(OrderingNode.Left) => () => node.Left++,
            _ => throw new ArgumentOutOfRangeException(nameof(property)),
        };

        if (interceptor)
        {
            callback.OnSaving = _ => changeHierarchy();
        }
        else
        {
            context.SavingChanges += (_, _) => changeHierarchy();
        }

        // Act
        var failure = await Record.ExceptionAsync(async () =>
        {
            if (synchronous)
            {
                // WHY: This branch exercises the explicitly synchronous public save boundary.
                // ReSharper disable once MethodHasAsyncOverload
                context.SaveChanges();
            }
            else
            {
                await context.SaveChangesAsync(CancellationToken.None);
            }
        });

        var stored = await setup
            .Set<OrderingNode>()
            .Where(value => value.Id == 1)
            .Select(value => new
            {
                value.Name,
                value.ParentId,
                value.Left,
            })
            .SingleAsync(CancellationToken.None);

        var storedMarker = await setup
            .Set<OrderingMarker>()
            .Select(value => value.Value)
            .SingleAsync(CancellationToken.None);

        // Assert
        var rejection = Assert.IsType<NestedSetException>(failure);
        Assert.Equal(NestedSetErrorCode.InvalidContext, rejection.Code);
        Assert.Equal(0, probe.PayloadCommands);
        Assert.Equal(0, probe.LockCommands);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal("original", storedMarker);
        Assert.Equal("Alpha", stored.Name);
        Assert.Equal(10, stored.ParentId);
        Assert.Equal(originalLeft, stored.Left);
    }

    /// <summary>
    ///     Leaves hierarchy edits introduced after a zero-write save outside the completed save boundary.
    /// </summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task SaveChanges_ManualCompletionHierarchyChanges_RemainPending(
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
        var probe = new SaveBoundaryProbe();
        var callback = new ManualSaveCompletionCallback();
        await using var context = CreateManualDetectionContext(setup, wrapped, probe, callback);
        var node = await context
            .Set<OrderingNode>()
            .SingleAsync(value => value.Id == 1, CancellationToken.None);

        context.ChangeTracker.AutoDetectChangesEnabled = false;
        var detections = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detections++;

        if (interceptor)
        {
            callback.OnSaved = () => node.Name = "completion edit";
        }
        else
        {
            context.SavedChanges += (_, _) => node.Name = "completion edit";
        }

        // Act
        // WHY: The synchronous variants verify the explicitly synchronous public save boundary.
        // ReSharper disable once MethodHasAsyncOverload
        var saved = synchronous
            ? context.SaveChanges()
            : await context.SaveChangesAsync(CancellationToken.None);

        var stored = await setup
            .Set<OrderingNode>()
            .Where(value => value.Id == 1)
            .Select(value => value.Name)
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(0, saved);
        Assert.Equal("Alpha", stored);
        Assert.Equal("completion edit", node.Name);
        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(node)
                .State);

        Assert.Equal(0, detections);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(0, probe.PayloadCommands);
        Assert.Equal(0, probe.LockCommands);
    }
}

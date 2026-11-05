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
    public async Task OrdinaryPayloadChangesHonorAutomaticDetection(
        bool wrapped,
        bool synchronous,
        bool nodePayload,
        bool automaticDetection
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedAsync(setup, cancellationToken: TestContext.Current.CancellationToken);
        await setup.AddAsync(new OrderingMarker { Id = 1, Value = "original" }, CancellationToken.None);
        await setup.SaveChangesAsync(CancellationToken.None);
        var probe = new SaveBoundaryProbe();
        await using var context = CreateManualDetectionContext(setup, wrapped, probe);
        var marker = await context.Set<OrderingMarker>().SingleAsync(CancellationToken.None);
        var node = await context.Set<OrderingNode>().SingleAsync(value => value.Id == 1, CancellationToken.None);
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
        var saved = synchronous ? context.SaveChanges() : await context.SaveChangesAsync(CancellationToken.None);
        var stored = nodePayload
            ? await setup.Set<OrderingNode>().Where(value => value.Id == 1)
                .Select(value => value.Payload).SingleAsync(CancellationToken.None)
            : await setup.Set<OrderingMarker>().Select(value => value.Value).SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(automaticDetection ? "ordinary change" : "original", stored);
        Assert.Equal(automaticDetection ? 1 : 0, saved);
        var expectedDetections = automaticDetection ? (wrapped && !synchronous ? 2 : 1) : 0;

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
    public async Task ExplicitPayloadMarksPreserveOtherUndetectedChanges(
        bool wrapped,
        bool synchronous,
        bool callbackWrite
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedAsync(setup, cancellationToken: TestContext.Current.CancellationToken);
        await setup.AddAsync(new OrderingMarker { Id = 1, Value = "original" }, CancellationToken.None);
        await setup.SaveChangesAsync(CancellationToken.None);
        var probe = new SaveBoundaryProbe();
        var callback = new SaveBoundaryCallback();
        await using var context = CreateManualDetectionContext(setup, wrapped, probe, callback);
        var marker = await context.Set<OrderingMarker>().SingleAsync(CancellationToken.None);
        var node = await context.Set<OrderingNode>().SingleAsync(value => value.Id == 1, CancellationToken.None);
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        var markerValue = context.Entry(marker).Property(value => value.Value);
        node.Payload = "undetected payload";
        var detections = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detections++;
        Action markPayload = () =>
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
        var saved = synchronous ? context.SaveChanges() : await context.SaveChangesAsync(CancellationToken.None);
        var storedMarker = await setup.Set<OrderingMarker>().Select(value => value.Value)
            .SingleAsync(CancellationToken.None);

        var storedPayload = await setup.Set<OrderingNode>().Where(value => value.Id == 1)
            .Select(value => value.Payload).SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, saved);
        Assert.Equal("explicitly marked", storedMarker);
        Assert.Equal("original", storedPayload);
        Assert.Equal(0, detections);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(1, probe.PayloadCommands);
        Assert.Equal(0, probe.LockCommands);
    }

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
    public async Task ManualCallbackPayloadRemainsUndetected(
        bool wrapped,
        bool synchronous,
        bool interceptor
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedAsync(setup, cancellationToken: TestContext.Current.CancellationToken);
        await setup.AddAsync(new OrderingMarker { Id = 1, Value = "original" }, CancellationToken.None);
        await setup.SaveChangesAsync(CancellationToken.None);
        var probe = new SaveBoundaryProbe();
        var callback = new SaveBoundaryCallback();
        await using var context = CreateManualDetectionContext(setup, wrapped, probe, callback);
        var marker = await context.Set<OrderingMarker>().SingleAsync(CancellationToken.None);
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
        var saved = synchronous ? context.SaveChanges() : await context.SaveChangesAsync(CancellationToken.None);
        var stored = await setup.Set<OrderingMarker>().Select(value => value.Value)
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
    public async Task ManualSaveRejectsLateHierarchyChanges(
        bool wrapped,
        bool synchronous,
        bool interceptor,
        string property,
        bool pendingWrite
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedAsync(setup, cancellationToken: TestContext.Current.CancellationToken);
        await setup.AddAsync(new OrderingMarker { Id = 1, Value = "original" }, CancellationToken.None);
        await setup.SaveChangesAsync(CancellationToken.None);
        var probe = new SaveBoundaryProbe();
        var callback = new SaveBoundaryCallback();
        await using var context = CreateManualDetectionContext(setup, wrapped, probe, callback);
        var marker = await context.Set<OrderingMarker>().SingleAsync(CancellationToken.None);
        var node = await context.Set<OrderingNode>().SingleAsync(value => value.Id == 1, CancellationToken.None);
        context.ChangeTracker.AutoDetectChangesEnabled = false;

        // WHY: Exercise both the IDatabase persistence boundary and EF's zero-write early exit.
        if (pendingWrite)
        {
            var markerValue = context.Entry(marker).Property(value => value.Value);
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

        var stored = await setup.Set<OrderingNode>().Where(value => value.Id == 1)
            .Select(value => new { value.Name, value.ParentId, value.Left }).SingleAsync(CancellationToken.None);

        var storedMarker = await setup.Set<OrderingMarker>().Select(value => value.Value)
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
    ///     Honors consumer callback detection choices while preserving the library's original caller setting.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CoordinatedCallbackCanChooseNativeDetectionPolicy(
        bool interceptor,
        bool automaticDetection
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedAsync(setup, cancellationToken: TestContext.Current.CancellationToken);
        await setup.AddAsync(new OrderingMarker { Id = 1, Value = "original" }, CancellationToken.None);
        await setup.SaveChangesAsync(CancellationToken.None);
        var callback = new SaveBoundaryCallback();
        await using var context = CreateManualDetectionContext(setup, wrapped: true, callback);
        var node = await context.Set<OrderingNode>().SingleAsync(value => value.Id == 1, CancellationToken.None);
        var marker = await context.Set<OrderingMarker>().SingleAsync(CancellationToken.None);
        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        context.Entry(node).Property(value => value.Name).CurrentValue = "Zulu";
        bool? observedPolicy = null;
        var detections = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detections++;
        Action choosePolicy = () =>
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
        var stored = await setup.Set<OrderingNode>().Where(value => value.Id == 1)
            .Select(value => new { value.Name, value.Payload }).SingleAsync(CancellationToken.None);

        var storedMarker = await setup.Set<OrderingMarker>().Select(value => value.Value)
            .SingleAsync(CancellationToken.None);

        // Assert
        var expectedPayload = automaticDetection ? "original" : "callback payload";
        Assert.Equal("Zulu", stored.Name);
        Assert.Equal(expectedPayload, stored.Payload);
        Assert.Equal(expectedPayload, storedMarker);
        Assert.Equal(automaticDetection, observedPolicy);
        Assert.Equal(1, detections);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>
    ///     Coordinates an explicitly marked parent edit without overriding manual change detection.
    /// </summary>
    [Fact]
    public async Task ManualDetectionCoordinatesExplicitParentChange()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedAsync(setup, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = CreateManualDetectionContext(setup, wrapped: true);
        var node = await context.Set<OrderingNode>().SingleAsync(value => value.Id == 1, CancellationToken.None);
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        node.ParentId = 2;
        context.Entry(node).Property(value => value.ParentId).IsModified = true;

        // Act
        await context.SaveChangesAsync(CancellationToken.None);
        var stored = await setup.Set<OrderingNode>().Where(value => value.Id == 1)
            .Select(value => new { value.ParentId, value.Depth, value.Left, value.Right })
            .SingleAsync(CancellationToken.None);

        var parent = await setup.Set<OrderingNode>().Where(value => value.Id == 2)
            .Select(value => new { value.Depth, value.Left, value.Right }).SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, stored.ParentId);
        Assert.Equal(parent.Depth + 1, stored.Depth);
        Assert.True(parent.Left < stored.Left);
        Assert.True(stored.Right < parent.Right);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
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
    public async Task ManualCompletionHierarchyChangesRemainPending(
        bool wrapped,
        bool synchronous,
        bool interceptor
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedAsync(setup, cancellationToken: TestContext.Current.CancellationToken);
        var probe = new SaveBoundaryProbe();
        var callback = new ManualSaveCompletionCallback();
        await using var context = CreateManualDetectionContext(setup, wrapped, probe, callback);
        var node = await context.Set<OrderingNode>().SingleAsync(value => value.Id == 1, CancellationToken.None);
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
        var saved = synchronous ? context.SaveChanges() : await context.SaveChangesAsync(CancellationToken.None);
        var stored = await setup.Set<OrderingNode>().Where(value => value.Id == 1)
            .Select(value => value.Name).SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(0, saved);
        Assert.Equal("Alpha", stored);
        Assert.Equal("completion edit", node.Name);
        Assert.Equal(EntityState.Unchanged, context.Entry(node).State);
        Assert.Equal(0, detections);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(0, probe.PayloadCommands);
        Assert.Equal(0, probe.LockCommands);
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
        var options = SaveBoundaryTestSupport.Options(setup, interceptors).Options;

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

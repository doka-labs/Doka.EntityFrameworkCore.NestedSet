namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>
    ///     Explicit hierarchy and application writes commit without detecting foreign or same-node raw payload.
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
    public async Task SaveChangesAsync_MarkedHierarchy_SavesOnlyExplicitWrites(
        bool parent,
        bool currentValue,
        bool acceptChanges
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        await SeedAsync(setup, cancellationToken: TestContext.Current.CancellationToken);
        await SeedManualMarkersAsync(setup, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = CreateManualDetectionContext(setup, wrapped: true);
        var node = await context
            .Set<OrderingNode>()
            .SingleAsync(value => value.Id == 1, CancellationToken.None);

        var rows = await context
            .Set<OrderingMarker>()
            .OrderBy(value => value.Id)
            .ToArrayAsync(CancellationToken.None);

        context.ChangeTracker.AutoDetectChangesEnabled = false;
        node.Payload = "unmarked node payload";
        rows[0].Value = "unmarked foreign payload";
        MarkManualHierarchyChange(context, node, parent, currentValue);
        context
            .Entry(node)
            .Property(value => value.Category)
            .CurrentValue = OrderingCategory.Beta;

        context
            .Entry(rows[1])
            .Property(value => value.Value)
            .CurrentValue = "explicit update";

        context.Remove(rows[2]);
        var addition = new OrderingMarker
        {
            Id = 4,
            Value = "explicit addition",
        };

        await context.AddAsync(addition, CancellationToken.None);
        var detections = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detections++;

        // Act
        await context.SaveChangesAsync(acceptChanges, CancellationToken.None);
        var storedNode = await setup
            .Set<OrderingNode>()
            .Where(value => value.Id == 1)
            .Select(value => new
            {
                value.ParentId,
                value.Name,
                value.Payload,
                value.Category,
            })
            .SingleAsync(CancellationToken.None);

        var storedRows = await setup
            .Set<OrderingMarker>()
            .OrderBy(value => value.Id)
            .Select(value => new
            {
                value.Id,
                value.Value,
            })
            .ToArrayAsync(CancellationToken.None);

        var order = await setup
            .Set<OrderingNode>()
            .OrderBy(value => value.Left)
            .Select(value => value.Id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(
            parent
                ? 2
                : 10,
            storedNode.ParentId);

        Assert.Equal(
            parent
                ? "Alpha"
                : "Zulu",
            storedNode.Name);

        Assert.Equal("original", storedNode.Payload);
        Assert.Equal(OrderingCategory.Beta, storedNode.Category);
        Assert.Equal([1, 2, 4], storedRows.Select(value => value.Id));
        Assert.Equal(["original", "explicit update", "explicit addition"], storedRows.Select(value => value.Value));
        Assert.Equal([10, 2, 1], order);
        Assert.Equal("unmarked node payload", node.Payload);
        Assert.Equal("unmarked foreign payload", rows[0].Value);
        Assert.Equal(
            acceptChanges
                ? EntityState.Unchanged
                : EntityState.Modified,
            context.Entry(rows[1])
                .State);

        Assert.Equal(
            acceptChanges
                ? EntityState.Detached
                : EntityState.Deleted,
            context.Entry(rows[2])
                .State);

        Assert.Equal(
            acceptChanges
                ? EntityState.Unchanged
                : EntityState.Added,
            context.Entry(addition)
                .State);

        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(rows[0])
                .State);

        Assert.False(
            context
                .Entry(node)
                .Property(value => value.Payload)
                .IsModified);

        Assert.Equal(0, detections);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>
    ///     Manual coordinated callbacks persist explicit writes while leaving their raw CLR payload unsaved.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SaveChangesAsync_ManualCoordinatedCallbacksSaveOnlyExplicit_Writes(
        bool parent,
        bool interceptor
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        await SeedAsync(setup, cancellationToken: TestContext.Current.CancellationToken);
        await SeedManualMarkersAsync(setup, cancellationToken: TestContext.Current.CancellationToken);
        var callback = new SaveBoundaryCallback();
        await using var context = CreateManualDetectionContext(setup, wrapped: true, callback);
        var node = await context
            .Set<OrderingNode>()
            .SingleAsync(value => value.Id == 1, CancellationToken.None);

        var rows = await context
            .Set<OrderingMarker>()
            .OrderBy(value => value.Id)
            .ToArrayAsync(CancellationToken.None);

        context.ChangeTracker.AutoDetectChangesEnabled = false;
        MarkManualHierarchyChange(context, node, parent, currentValue: true);
        var addition = new OrderingMarker
        {
            Id = 4,
            Value = "callback addition",
        };

        bool? callbackPolicy = null;
        Action<DbContext> stageWrites = current =>
        {
            callbackPolicy = current.ChangeTracker.AutoDetectChangesEnabled;
            rows[0].Value = "raw callback foreign";
            node.Payload = "raw callback node";
            current
                .Entry(rows[1])
                .Property(value => value.Value)
                .CurrentValue = "callback update";

            current
                .Entry(node)
                .Property(value => value.Category)
                .CurrentValue = OrderingCategory.Beta;

            current.Remove(rows[2]);

            // WHY: This synchronous callback attaches an explicitly added row before the native save builds SQL.
            // ReSharper disable once MethodHasAsyncOverload
            current.Add(addition);
        };

        if (interceptor)
        {
            callback.OnSaving = stageWrites;
        }
        else
        {
            context.SavingChanges += (_, _) => stageWrites(context);
        }

        var detections = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detections++;

        // Act
        await context.SaveChangesAsync(CancellationToken.None);
        var storedNode = await setup
            .Set<OrderingNode>()
            .Where(value => value.Id == 1)
            .Select(value => new
            {
                value.ParentId,
                value.Name,
                value.Payload,
                value.Category,
            })
            .SingleAsync(CancellationToken.None);

        var storedRows = await setup
            .Set<OrderingMarker>()
            .OrderBy(value => value.Id)
            .Select(value => new
            {
                value.Id,
                value.Value,
            })
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(
            parent
                ? 2
                : 10,
            storedNode.ParentId);

        Assert.Equal(
            parent
                ? "Alpha"
                : "Zulu",
            storedNode.Name);

        Assert.Equal("original", storedNode.Payload);
        Assert.Equal(OrderingCategory.Beta, storedNode.Category);
        Assert.Equal([1, 2, 4], storedRows.Select(value => value.Id));
        Assert.Equal(["original", "callback update", "callback addition"], storedRows.Select(value => value.Value));
        Assert.Equal(false, callbackPolicy);
        Assert.Equal(0, detections);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Seeds foreign payload and explicit update/delete targets before a manual save starts.</summary>
    private static async Task SeedManualMarkersAsync(
        DbContext context,
        CancellationToken cancellationToken
    )
    {
        await context.AddRangeAsync(
            [
                new OrderingMarker
                {
                    Id = 1,
                    Value = "original",
                },
                new OrderingMarker
                {
                    Id = 2,
                    Value = "original",
                },
                new OrderingMarker
                {
                    Id = 3,
                    Value = "original",
                },
            ],
            cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Fails after the native payload save so the transaction must restore explicit pending writes.</summary>
    private sealed class ManualSaveFailure : SaveChangesInterceptor
    {
        /// <summary>Gets the exact failure expected after native persistence.</summary>
        internal Exception Error { get; } = new InvalidOperationException("Injected manual save failure.");

        /// <inheritdoc />
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        ) => throw Error;
    }
}

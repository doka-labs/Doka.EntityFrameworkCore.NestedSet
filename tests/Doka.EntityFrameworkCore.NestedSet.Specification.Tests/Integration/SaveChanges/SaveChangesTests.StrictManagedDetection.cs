namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    private static readonly Guid s_manualDestinationTree = Guid.Parse("a0100000-0000-0000-0000-000000000001");
    private static readonly Guid s_manualFirstImportTree = Guid.Parse("a0100000-0000-0000-0000-000000000002");
    private static readonly Guid s_manualSecondImportTree = Guid.Parse("a0100000-0000-0000-0000-000000000003");

    /// <summary>
    ///     Single, subtree and forest inserts honor manual tracking for existing hierarchy and foreign payload.
    /// </summary>
    [Theory]
    [InlineData("single", false, false)]
    [InlineData("single", false, true)]
    [InlineData("single", true, false)]
    [InlineData("single", true, true)]
    [InlineData("subtree", false, false)]
    [InlineData("subtree", false, true)]
    [InlineData("subtree", true, false)]
    [InlineData("subtree", true, true)]
    [InlineData("forest", false, false)]
    [InlineData("forest", false, true)]
    [InlineData("forest", true, false)]
    [InlineData("forest", true, true)]
    public async Task InsertManualShapeAsync_ManualManagedInsertion_SavesOnlyExplicitWrites(
        string shape,
        bool callbackWrite,
        bool marked
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        await SeedManualDestinationAsync(setup, cancellationToken: TestContext.Current.CancellationToken);
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
        var addition = new OrderingMarker
        {
            Id = 4,
            Value = "explicit addition",
        };

        var observedPolicies = new List<bool>();
        var stagePayload = () =>
        {
            rows[0].Value = "changed payload";
            node.Payload = "changed payload";
        };

        if (!callbackWrite)
        {
            stagePayload();
        }

        var writesStaged = false;
        callback.OnSaving = current =>
        {
            observedPolicies.Add(current.ChangeTracker.AutoDetectChangesEnabled);

            if (callbackWrite)
            {
                stagePayload();
            }

            if (!writesStaged)
            {
                // WHY: Facade preflight rejects registered pending writes; callback writes join its owned save.
                current
                    .Entry(rows[1])
                    .Property(value => value.Value)
                    .CurrentValue = "explicit update";

                current.Remove(rows[2]);

                // WHY: This synchronous callback attaches an explicit row before the native SQL write set is built.
                // ReSharper disable once MethodHasAsyncOverload
                current.Add(addition);

                if (marked)
                {
                    current
                        .Entry(rows[0])
                        .Property(value => value.Value)
                        .IsModified = true;

                    current
                        .Entry(node)
                        .Property(value => value.Payload)
                        .IsModified = true;
                }

                writesStaged = true;
            }
        };

        var detections = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detections++;
        var inputs = ManualInsertionInputs(shape);

        // Act
        await InsertManualShapeAsync(context, shape, inputs, cancellationToken: TestContext.Current.CancellationToken);
        var storedNode = await setup
            .Set<OrderingNode>()
            .Where(value => value.Id == 1)
            .Select(value => value.Payload)
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

        var count = await setup
            .Set<OrderingNode>()
            .CountAsync(CancellationToken.None);

        // Assert
        Assert.Equal(
            marked
                ? "changed payload"
                : "original",
            storedNode);

        Assert.Equal([1, 2, 4], storedRows.Select(value => value.Id));
        Assert.Equal(
            [
                marked
                    ? "changed payload"
                    : "original",
                "explicit update", "explicit addition",
            ],
            storedRows.Select(value => value.Value));

        Assert.Equal(4 + inputs.Length, count);
        Assert.All(
            inputs,
            input => Assert.Equal(
                EntityState.Detached,
                context.Entry(input)
                    .State));

        Assert.NotEmpty(observedPolicies);
        Assert.All(observedPolicies, policy => Assert.False(policy));
        Assert.Equal(0, detections);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>
    ///     Managed failure restores explicitly pending rows without detecting initially raw foreign payload.
    /// </summary>
    [Theory]
    [InlineData("single")]
    [InlineData("subtree")]
    [InlineData("forest")]
    public async Task InsertManualShapeAsync_ManualManagedFailure_RestoresExplicitPendingWrites(
        string shape
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        await SeedManualDestinationAsync(setup, cancellationToken: TestContext.Current.CancellationToken);
        await SeedManualMarkersAsync(setup, cancellationToken: TestContext.Current.CancellationToken);
        var callback = new SaveBoundaryCallback();
        var failure = new ManualSaveFailure();
        await using var context = CreateManualDetectionContext(setup, wrapped: true, callback, failure);
        var rows = await context
            .Set<OrderingMarker>()
            .OrderBy(value => value.Id)
            .ToArrayAsync(CancellationToken.None);

        context.ChangeTracker.AutoDetectChangesEnabled = false;
        rows[0].Value = "unmarked foreign";
        var addition = new OrderingMarker
        {
            Id = 4,
            Value = "explicit addition",
        };

        callback.OnSaving = current =>
        {
            // WHY: These explicit callback writes are admitted after the facade's clean-context preflight.
            current
                .Entry(rows[1])
                .Property(value => value.Value)
                .CurrentValue = "explicit update";

            current.Remove(rows[2]);

            // WHY: This synchronous callback must register the addition before native persistence begins.
            // ReSharper disable once MethodHasAsyncOverload
            current.Add(addition);
        };

        var inputs = ManualInsertionInputs(shape);
        var detections = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detections++;

        // Act
        var error = await Record.ExceptionAsync(() => InsertManualShapeAsync(
            context,
            shape,
            inputs,
            cancellationToken: TestContext.Current.CancellationToken));

        var storedRows = await setup
            .Set<OrderingMarker>()
            .OrderBy(value => value.Id)
            .Select(value => value.Value)
            .ToArrayAsync(CancellationToken.None);

        var count = await setup
            .Set<OrderingNode>()
            .CountAsync(CancellationToken.None);

        // Assert
        Assert.Same(failure.Error, error);
        Assert.Equal(["original", "original", "original"], storedRows);
        Assert.Equal(4, count);
        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(rows[0])
                .State);

        Assert.Equal(
            EntityState.Modified,
            context.Entry(rows[1])
                .State);

        Assert.Equal(
            EntityState.Deleted,
            context.Entry(rows[2])
                .State);

        Assert.Equal(
            EntityState.Added,
            context.Entry(addition)
                .State);

        Assert.All(
            inputs,
            input => Assert.Equal(
                EntityState.Detached,
                context.Entry(input)
                    .State));

        Assert.Equal(0, detections);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Managed save callbacks can deliberately choose native detection without a library override.</summary>
    [Theory]
    [InlineData("single", false)]
    [InlineData("single", true)]
    [InlineData("subtree", false)]
    [InlineData("subtree", true)]
    [InlineData("forest", false)]
    [InlineData("forest", true)]
    public async Task InsertManualShapeAsync_ManagedCallbacks_CanChooseNativeDetectionPolicy(
        string shape,
        bool automaticDetection
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode: "Strict");

        await SeedManualDestinationAsync(setup, cancellationToken: TestContext.Current.CancellationToken);
        await SeedManualMarkersAsync(setup, cancellationToken: TestContext.Current.CancellationToken);
        var callback = new SaveBoundaryCallback();
        await using var context = CreateManualDetectionContext(setup, wrapped: true, callback);
        var node = await context
            .Set<OrderingNode>()
            .SingleAsync(value => value.Id == 1, CancellationToken.None);

        var row = await context
            .Set<OrderingMarker>()
            .SingleAsync(value => value.Id == 1, CancellationToken.None);

        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        var observedPolicies = new List<bool>();
        callback.OnSaving = current =>
        {
            observedPolicies.Add(current.ChangeTracker.AutoDetectChangesEnabled);
            current.ChangeTracker.AutoDetectChangesEnabled = !automaticDetection;
            node.Payload = "chosen callback payload";
            row.Value = "chosen callback payload";
        };

        var inputs = ManualInsertionInputs(shape);

        // Act
        await InsertManualShapeAsync(context, shape, inputs, cancellationToken: TestContext.Current.CancellationToken);
        var storedNode = await setup
            .Set<OrderingNode>()
            .Where(value => value.Id == 1)
            .Select(value => value.Payload)
            .SingleAsync(CancellationToken.None);

        var storedRow = await setup
            .Set<OrderingMarker>()
            .Where(value => value.Id == 1)
            .Select(value => value.Value)
            .SingleAsync(CancellationToken.None);

        var count = await setup
            .Set<OrderingNode>()
            .CountAsync(CancellationToken.None);

        // Assert
        var expectedPayload = automaticDetection
            ? "original"
            : "chosen callback payload";

        Assert.Equal(expectedPayload, storedNode);
        Assert.Equal(expectedPayload, storedRow);
        Assert.Equal(4 + inputs.Length, count);
        Assert.NotEmpty(observedPolicies);
        Assert.All(observedPolicies, policy => Assert.Equal(automaticDetection, policy));
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Seeds a tracked-safe unaffected tree and a separate insertion destination.</summary>
    private static async Task SeedManualDestinationAsync(
        DbContext context,
        CancellationToken cancellationToken
    )
    {
        await SeedAsync(context, cancellationToken: cancellationToken);
        await context
            .NestedSet<OrderingNode>()
            .ForScope(1)
            .InsertRootAsync(
                new OrderingNode
                {
                    Id = 100,
                    Name = "Destination",
                },
                s_manualDestinationTree,
                cancellationToken);
    }
}

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Managed rollback restores unchanged sharing peers promoted by explicit application callbacks.</summary>
    [Theory]
    [InlineData("owned", false, true, false)]
    [InlineData("owned", true, false, false)]
    [InlineData("owned", false, false, true)]
    [InlineData("split", false, false, false)]
    [InlineData("split", true, true, false)]
    [InlineData("split", true, false, true)]
    public async Task ManagedSharingRollbackRestoresPromotedPeer(
        string shape,
        bool bulk,
        bool concurrency,
        bool reverse
    )
    {
        // Arrange
        await using var database = await CreateManagedSharingDatabaseAsync(
            shape,
            cancellationToken: TestContext.Current.CancellationToken);

        var callback = new SaveBoundaryCallback();
        var failure = new PlannedParentFailure();
        IInterceptor[] interceptors = concurrency
            ? [callback]
            : [callback, failure];

        await using var context = await CreateManagedSharingContextAsync(
            database,
            shape,
            cancellationToken: TestContext.Current.CancellationToken,
            interceptors);

        var graph = await PrepareManagedSharingGraphAsync(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

        NavigationNode[] inputs = bulk
            ?
            [
                new NavigationNode
                {
                    Id = 101,
                    Name = "Inserted",
                },
                new NavigationNode
                {
                    Id = 102,
                    Name = "Child",
                },
            ]
            :
            [
                new NavigationNode
                {
                    Id = 101,
                    Name = "Inserted",
                },
            ];

        var inputBefore = CapturePreparedOwnerInputs(inputs);
        PlannedEntryState[]? pending = null;
        EntityState? counterpartState = null;
        callback.OnSaving = current =>
        {
            callback.OnSaving = null;
            StageManagedSharingCallback(graph, reverse);
            counterpartState = reverse
                ? graph.Row.State
                : graph.Payload.State;

            // WHY: EF has not yet promoted sharing peers; this is the exact pending callback write baseline.
            pending = CaptureManagedSharingState(current, graph);
        };

        if (concurrency)
        {
            await ChangeManagedSharingVersionAsync(
                database,
                shape,
                cancellationToken: TestContext.Current.CancellationToken);
        }

        var storedBefore = await ReadManagedSharingRowAsync(
            database,
            shape,
            cancellationToken: TestContext.Current.CancellationToken);

        var hierarchyBefore = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var error = await Record.ExceptionAsync(() => InsertManagedSharingShapeAsync(
            context,
            inputs,
            bulk,
            cancellationToken: TestContext.Current.CancellationToken));

        var after = CaptureManagedSharingState(context, graph);
        var storedAfter = await ReadManagedSharingRowAsync(
            database,
            shape,
            cancellationToken: TestContext.Current.CancellationToken);

        var hierarchyAfter = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(pending);
        Assert.Equal(EntityState.Unchanged, counterpartState);
        AssertPlannedParentState(pending, after);

        if (concurrency)
        {
            Assert.IsType<DbUpdateConcurrencyException>(error);
        }
        else
        {
            Assert.Same(failure.Error, error);
        }

        Assert.Equal(inputBefore, CapturePreparedOwnerInputs(inputs));
        Assert.All(
            inputs,
            input => Assert.Equal(
                EntityState.Detached,
                context.Entry(input)
                    .State));

        Assert.Equal(storedBefore, storedAfter);
        Assert.Equal(hierarchyBefore.Nodes, hierarchyAfter.Nodes);
        Assert.Equal("sentinel", graph.Sentinel.Entity.Value);
        Assert.Equal(EntityState.Unchanged, graph.Sentinel.State);
        Assert.Equal(hierarchyBefore.Note, hierarchyAfter.Note);
        Assert.Equal(0, hierarchyAfter.Issues);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Restored callback writes retry without manually repairing a provider-promoted sharing entry.</summary>
    [Theory]
    [InlineData("owned", true)]
    [InlineData("split", false)]
    public async Task ManagedSharingRollbackAllowsExplicitWriteRetry(
        string shape,
        bool bulk
    )
    {
        // Arrange
        await using var database = await CreateManagedSharingDatabaseAsync(
            shape,
            cancellationToken: TestContext.Current.CancellationToken);

        var callback = new SaveBoundaryCallback();
        var failure = new PlannedParentFailure();
        await using var context = await CreateManagedSharingContextAsync(
            database,
            shape,
            cancellationToken: TestContext.Current.CancellationToken,
            callback,
            failure);

        var graph = await PrepareManagedSharingGraphAsync(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

        NavigationNode[] inputs = bulk
            ?
            [
                new NavigationNode
                {
                    Id = 101,
                    Name = "Inserted",
                },
                new NavigationNode
                {
                    Id = 102,
                    Name = "Child",
                },
            ]
            :
            [
                new NavigationNode
                {
                    Id = 101,
                    Name = "Inserted",
                },
            ];

        PlannedEntryState[]? pending = null;
        callback.OnSaving = current =>
        {
            callback.OnSaving = null;
            StageManagedSharingCallback(graph, reverse: false);
            pending = CaptureManagedSharingState(current, graph);
        };

        var hierarchyBefore = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var detections = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detections++;

        // WHY: A failed insertion leaves explicit callback writes pending. Ordinary save is their supported retry.
        var firstError = await Record.ExceptionAsync(() => InsertManagedSharingShapeAsync(
            context,
            inputs,
            bulk,
            cancellationToken: TestContext.Current.CancellationToken));

        var afterFailure = CaptureManagedSharingState(context, graph);

        // Act
        var retryError = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var storedAfter = await ReadManagedSharingRowAsync(
            database,
            shape,
            cancellationToken: TestContext.Current.CancellationToken);

        var hierarchyAfter = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(pending);
        AssertPlannedParentState(pending, afterFailure);
        Assert.Same(failure.Error, firstError);
        Assert.Null(retryError);
        Assert.Equal("callback row", storedAfter.Text);
        Assert.Equal("original payload", storedAfter.Payload);
        Assert.Equal("raw pending payload", graph.Payload.Entity.Value);
        Assert.Equal(EntityState.Unchanged, graph.Row.State);
        Assert.Equal(EntityState.Unchanged, graph.Payload.State);
        Assert.All(graph.Payload.Properties, property => Assert.False(property.IsModified));
        Assert.Equal(hierarchyBefore.Nodes, hierarchyAfter.Nodes);
        Assert.All(
            inputs,
            input => Assert.Equal(
                EntityState.Detached,
                context.Entry(input)
                    .State));

        Assert.Equal("sentinel", graph.Sentinel.Entity.Value);
        Assert.Equal(EntityState.Unchanged, graph.Sentinel.State);
        Assert.Equal(hierarchyBefore.Note, hierarchyAfter.Note);
        Assert.Equal(0, hierarchyAfter.Issues);
        Assert.Equal(0, detections);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Managed writes commit explicit payload atomically while raw counterpart edits stay unsaved.</summary>
    [Theory]
    [InlineData("owned", false, false)]
    [InlineData("owned", true, true)]
    [InlineData("split", true, false)]
    [InlineData("split", false, true)]
    public async Task ManagedSharingSuccessPersistsOnlyExplicitCallbackWrites(
        string shape,
        bool bulk,
        bool reverse
    )
    {
        // Arrange
        await using var database = await CreateManagedSharingDatabaseAsync(
            shape,
            cancellationToken: TestContext.Current.CancellationToken);

        var callback = new SaveBoundaryCallback();
        await using var context = await CreateManagedSharingContextAsync(
            database,
            shape,
            cancellationToken: TestContext.Current.CancellationToken,
            callback);

        var graph = await PrepareManagedSharingGraphAsync(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

        NavigationNode[] inputs = bulk
            ?
            [
                new NavigationNode
                {
                    Id = 101,
                    Name = "Inserted",
                },
                new NavigationNode
                {
                    Id = 102,
                    Name = "Child",
                },
            ]
            :
            [
                new NavigationNode
                {
                    Id = 101,
                    Name = "Inserted",
                },
            ];

        callback.OnSaving = _ =>
        {
            callback.OnSaving = null;
            StageManagedSharingCallback(graph, reverse);
        };

        var hierarchyBefore = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var detections = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detections++;

        // Act
        var error = await Record.ExceptionAsync(() => InsertManagedSharingShapeAsync(
            context,
            inputs,
            bulk,
            cancellationToken: TestContext.Current.CancellationToken));

        var storedAfter = await ReadManagedSharingRowAsync(
            database,
            shape,
            cancellationToken: TestContext.Current.CancellationToken);

        var hierarchyAfter = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(error);
        Assert.Equal(
            reverse
                ? "original row"
                : "callback row",
            storedAfter.Text);

        Assert.Equal(
            reverse
                ? "callback payload"
                : "original payload",
            storedAfter.Payload);

        Assert.Equal(
            reverse
                ? "raw pending row"
                : "callback row",
            graph.Row.Entity.Text);

        Assert.Equal(
            reverse
                ? "callback payload"
                : "raw pending payload",
            graph.Payload.Entity.Value);

        Assert.Equal(EntityState.Unchanged, graph.Row.State);
        Assert.Equal(EntityState.Unchanged, graph.Payload.State);
        Assert.All(graph.Row.Properties, property => Assert.False(property.IsModified));
        Assert.All(graph.Payload.Properties, property => Assert.False(property.IsModified));
        Assert.Equal(hierarchyBefore.Nodes.Length + inputs.Length, hierarchyAfter.Nodes.Length);
        Assert.All(inputs, input => Assert.Contains(hierarchyAfter.Nodes, node => node.Id == input.Id));
        Assert.All(
            inputs,
            input => Assert.Equal(
                EntityState.Detached,
                context.Entry(input)
                    .State));

        Assert.Equal("sentinel", graph.Sentinel.Entity.Value);
        Assert.Equal(EntityState.Unchanged, graph.Sentinel.State);
        Assert.Equal(hierarchyBefore.Note, hierarchyAfter.Note);
        Assert.Equal(0, hierarchyAfter.Issues);
        Assert.Equal(0, detections);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }
}

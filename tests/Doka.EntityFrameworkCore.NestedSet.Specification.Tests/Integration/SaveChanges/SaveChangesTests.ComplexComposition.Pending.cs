namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Pending added or deleted complex nodes retain exact lanes and persist after rollback.</summary>
    [Theory]
    [InlineData("coordinated", "outer", false)]
    [InlineData("coordinated", "nested", true)]
    [InlineData("single", "outer", true)]
    [InlineData("single", "nested", false)]
    [InlineData("subtree", "outer-clear", false)]
    [InlineData("subtree", "nested", true)]
    [InlineData("forest", "outer-clear", true)]
    [InlineData("forest", "nested", false)]
    [InlineData("coordinated", "deleted", true)]
    [InlineData("coordinated", "deleted", false)]
    [InlineData("single", "deleted", true)]
    [InlineData("subtree", "deleted", false)]
    [InlineData("forest", "deleted", true)]
    public async Task ComplexCompositionPendingGraphRetriesWithoutStagingHoles(
        string shape,
        string pending,
        bool automaticDetection
    )
    {
        // Arrange
        await using var database = await CreateComplexCompositionDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var callback = new SaveBoundaryCallback();
        Action? mutateOnCompletion = null;
        var failure = new ComplexCallbackCompletionFailure(() => mutateOnCompletion?.Invoke());
        await using var context = await CreateComplexCallbackContextAsync(database,
            cancellationToken: TestContext.Current.CancellationToken,
            callback, failure);

        var graph = await LoadComplexCallbackGraphAsync(context,
            cancellationToken: TestContext.Current.CancellationToken);

        var inputs = CreateComplexCompositionInputs(shape);
        await PrepareComplexCompositionOperationAsync(context, shape,
            cancellationToken: TestContext.Current.CancellationToken);
        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        PlannedEntryState[]? before = null;
        ComplexCallbackClrState? clrBefore = null;
        ComplexCompositionNativeState? nativeBefore = null;
        ComplexCallbackStoredRow? expectedAfterRetry = null;
        var inputBefore = CapturePreparedOwnerInputs(inputs);
        var storedBefore = await ReadComplexCallbackRowAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        var hierarchyBefore = await ReadPreparedOwnerStoreAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        Action mutation = () =>
        {
            ChangePendingComplexComposition(graph.Row.Entity, pending);
            // WHY: Force EF to create the callback's larger native child lane; raw CLR equality alone misses it.
            context.ChangeTracker.DetectChanges();
        };

        if (shape == "coordinated")
        {
            AddPendingComplexComposition(graph.Row.Entity, pending);
            context.ChangeTracker.DetectChanges();
            CaptureBaseline();
            callback.OnSaving = _ =>
            {
                callback.OnSaving = null;
                mutation();
            };
        }
        else
        {
            callback.OnSaving = current =>
            {
                callback.OnSaving = null;
                // WHY: Facade entry rejects preexisting writes. This callback creates the pending Added graph
                // before the managed snapshot, and the completion callback mutates it after that snapshot.
                graph.Row.Property(value => value.Label).CurrentValue = "explicit composed payload";
                AddPendingComplexComposition(graph.Row.Entity, pending);
                current.ChangeTracker.DetectChanges();
                CaptureBaseline();
            };

            // WHY: The managed snapshot includes SavingChanges writes, so its adversarial mutation is later.
            mutateOnCompletion = mutation;
        }

        void CaptureBaseline()
        {
            // WHY: Only initialize lanes at the actual baseline; later observations must expose stale slots.
            _ = NestedSetTrackerSnapshot.Capture(context);
            before = CaptureComplexCompositionEntries(context, graph);
            clrBefore = CaptureComplexCallbackClr(graph.Row.Entity);
            nativeBefore = CaptureComplexCompositionNative(graph);
            expectedAfterRetry = CaptureComplexCompositionStored(graph.Row.Entity);
        }

        var firstError = await Record.ExceptionAsync(() =>
            RunComplexCompositionOperationAsync(context, shape, inputs,
                cancellationToken: TestContext.Current.CancellationToken));

        var afterFailure = CaptureComplexCompositionEntries(context, graph);
        var clrAfterFailure = CaptureComplexCallbackClr(graph.Row.Entity);
        var nativeAfterFailure = CaptureComplexCompositionNative(graph);
        var storedAfterFailure = await ReadComplexCallbackRowAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        var hierarchyAfterFailure = await ReadPreparedOwnerStoreAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var retryError = await Record.ExceptionAsync(async () =>
        {
            context.ChangeTracker.DetectChanges();
            await context.SaveChangesAsync(CancellationToken.None);
        });

        var storedAfterRetry = await ReadComplexCallbackRowAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        var hierarchyAfterRetry = await ReadPreparedOwnerStoreAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(failure.Error, firstError);
        Assert.NotNull(before);
        Assert.NotNull(clrBefore);
        Assert.NotNull(nativeBefore);
        Assert.NotNull(expectedAfterRetry);
        AssertPlannedParentState(before, afterFailure);
        AssertComplexCallbackClr(clrBefore, clrAfterFailure);
        AssertComplexCompositionNative(nativeBefore, nativeAfterFailure);
        var pendingState = pending == "deleted" ? EntityState.Deleted : EntityState.Added;
        Assert.Contains(nativeBefore.Owners, owner => owner.State == pendingState);
        Assert.Equal(storedBefore, storedAfterFailure);
        Assert.Equal(hierarchyBefore.Nodes, hierarchyAfterFailure.Nodes);
        Assert.Equal(hierarchyBefore.Note, hierarchyAfterFailure.Note);
        Assert.Equal(0, hierarchyAfterFailure.Issues);
        Assert.Null(retryError);
        Assert.Equal(expectedAfterRetry, storedAfterRetry);
        AssertComplexCallbackClr(clrBefore, CaptureComplexCallbackClr(graph.Row.Entity));
        AssertComplexCompositionHierarchyAfterRetry(shape, hierarchyBefore, hierarchyAfterRetry);
        Assert.Equal(inputBefore, CapturePreparedOwnerInputs(inputs));
        Assert.All(inputs, input => Assert.Equal(EntityState.Detached, context.Entry(input).State));
        Assert.Equal("sentinel", graph.Sentinel.Entity.Value);
        Assert.Equal(EntityState.Unchanged, graph.Sentinel.State);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }
}

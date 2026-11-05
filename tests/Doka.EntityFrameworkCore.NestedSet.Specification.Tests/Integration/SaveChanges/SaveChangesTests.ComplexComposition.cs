namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Composed callback changes restore asymmetric native lanes before an unchanged-context retry.</summary>
    [Theory]
    [InlineData("coordinated", "remove-two", false)]
    [InlineData("coordinated", "nested-add-reverse", true)]
    [InlineData("single", "remove-two", false)]
    [InlineData("single", "nested-add-reverse", true)]
    [InlineData("subtree", "remove-two", true)]
    [InlineData("subtree", "nested-add-reverse", false)]
    [InlineData("forest", "remove-two", false)]
    [InlineData("forest", "nested-add-reverse", true)]
    public async Task ComplexCompositionRollbackRestoresNativeLanesAndAllowsRetry(
        string shape,
        string mutation,
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

        if (shape == "coordinated")
        {
            CaptureBaseline();
            callback.OnSaving = _ =>
            {
                callback.OnSaving = null;
                ChangeComplexComposition(graph.Row.Entity, mutation);
            };
        }
        else
        {
            callback.OnSaving = _ =>
            {
                callback.OnSaving = null;
                // WHY: Facades require a clean tracker at entry. Ordinary writes introduced by SavingChanges
                // belong to their managed baseline, unlike unrelated writes pending before facade preflight.
                graph.Row.Property(value => value.Label).CurrentValue = "explicit composed payload";
                CaptureBaseline();
            };

            // WHY: Managed callbacks are captured after SavingChanges; mutate only after the real payload save.
            mutateOnCompletion = () => ChangeComplexComposition(graph.Row.Entity, mutation);
        }

        void CaptureBaseline()
        {
            // WHY: Initialize lazy native lanes once at the accepted boundary, then observe caches independently.
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

    /// <summary>Valid composed callbacks persist their intended topology through every hierarchy save form.</summary>
    [Theory]
    [InlineData("coordinated", "remove-two")]
    [InlineData("single", "nested-add-reverse")]
    [InlineData("subtree", "remove-two")]
    [InlineData("forest", "nested-add-reverse")]
    public async Task ComplexCompositionSuccessPersistsConsumerTopology(string shape, string mutation)
    {
        // Arrange
        await using var database = await CreateComplexCompositionDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var callback = new SaveBoundaryCallback();
        await using var context = await CreateComplexCallbackContextAsync(database,
            cancellationToken: TestContext.Current.CancellationToken,
            callback);

        var graph = await LoadComplexCallbackGraphAsync(context,
            cancellationToken: TestContext.Current.CancellationToken);

        var inputs = CreateComplexCompositionInputs(shape);
        await PrepareComplexCompositionOperationAsync(context, shape,
            cancellationToken: TestContext.Current.CancellationToken);
        var hierarchyBefore = await ReadPreparedOwnerStoreAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        ComplexCallbackClrState? intended = null;
        ComplexCallbackStoredRow? expected = null;
        callback.OnSaving = current =>
        {
            callback.OnSaving = null;
            if (shape != "coordinated")
            {
                // WHY: Ordinary callback writes are allowed after a facade's clean-tracker preflight.
                graph.Row.Property(value => value.Label).CurrentValue = "explicit composed payload";
            }

            ChangeComplexComposition(graph.Row.Entity, mutation);
            // WHY: The consumer explicitly opts these ordinary JSON changes into this save with detection off.
            current.ChangeTracker.DetectChanges();
            intended = CaptureComplexCallbackClr(graph.Row.Entity);
            expected = CaptureComplexCompositionStored(graph.Row.Entity);
        };

        // Act
        var error = await Record.ExceptionAsync(() =>
            RunComplexCompositionOperationAsync(context, shape, inputs,
                cancellationToken: TestContext.Current.CancellationToken));

        var storedAfter = await ReadComplexCallbackRowAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        var hierarchyAfter = await ReadPreparedOwnerStoreAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(error);
        Assert.NotNull(intended);
        Assert.NotNull(expected);
        AssertComplexCallbackClr(intended, CaptureComplexCallbackClr(graph.Row.Entity));
        Assert.Equal(expected, storedAfter);
        Assert.Equal(hierarchyBefore.Nodes.Length + inputs.Length, hierarchyAfter.Nodes.Length);
        Assert.Equal("sentinel", hierarchyAfter.Note);
        Assert.Equal(0, hierarchyAfter.Issues);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }
}

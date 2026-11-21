namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Managed recovery restores ordinary complex writes changed after the managed snapshot.</summary>
    [Theory]
    [InlineData("single", "remove", false)]
    [InlineData("subtree", "nested-clear", true)]
    [InlineData("forest", "list-null", false)]
    [InlineData("forest", "reference-null", true)]
    public async Task InsertComplexCallbackInputsAsync_ComplexCallbackManagedCompletion_RestoresPendingWrite(
        string shape,
        string mutation,
        bool automaticDetection
    )
    {
        // Arrange
        await using var database = await CreateComplexCallbackDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var callback = new SaveBoundaryCallback();
        ComplexCallbackRow? callbackRow = null;
        var failure = new ComplexCallbackCompletionFailure(() => ChangeComplexCallbackRow(
            callbackRow ?? throw new InvalidOperationException("The callback row was not loaded."),
            mutation));

        await using var context = await CreateComplexCallbackContextAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken,
            callback,
            failure);

        var graph = await LoadComplexCallbackGraphAsync(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

        callbackRow = graph.Row.Entity;
        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        var inputs = Enumerable
            .Range(
                101,
                shape == "single"
                    ? 1
                    : shape == "subtree"
                        ? 2
                        : 4)
            .Select(id => new NavigationNode
            {
                Id = id,
                Name = "Input " + id.ToString(CultureInfo.InvariantCulture),
            })
            .ToArray();

        var inputBefore = CapturePreparedOwnerInputs(inputs);
        var clrBefore = CaptureComplexCallbackClr(graph.Row.Entity);
        var nativeBefore = CaptureComplexCallbackNative(graph);
        var storedBefore = await ReadComplexCallbackRowAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var hierarchyBefore = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        PlannedEntryState[]? pending = null;
        callback.OnSaving = current =>
        {
            callback.OnSaving = null;
            graph.Row.Property(value => value.Label)
                .CurrentValue = "explicit callback row";

            // WHY: SavingChanges writes belong to the managed baseline. The destructive mutation occurs later,
            // in SavedChanges, after this exact pending ordinary write has been captured by the library.
            pending = CapturePlannedParentState(current)
                .Where(entry => ReferenceEquals(entry.Entity, callbackRow)
                    || ReferenceEquals(entry.Entity, graph.Sentinel.Entity))
                .ToArray();
        };

        // Act
        var error = await Record.ExceptionAsync(() => InsertComplexCallbackInputsAsync(
            context,
            shape,
            inputs,
            cancellationToken: TestContext.Current.CancellationToken));

        var storedAfter = await ReadComplexCallbackRowAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var hierarchyAfter = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(failure.Error, error);
        Assert.NotNull(pending);
        AssertPlannedParentState(
            pending,
            CapturePlannedParentState(context)
                .Where(entry =>
                    ReferenceEquals(entry.Entity, callbackRow) || ReferenceEquals(entry.Entity, graph.Sentinel.Entity))
                .ToArray());

        AssertComplexCallbackClr(clrBefore, CaptureComplexCallbackClr(graph.Row.Entity));
        AssertComplexCallbackNative(nativeBefore, CaptureComplexCallbackNative(graph));
        Assert.Equal(storedBefore, storedAfter);
        Assert.Equal(hierarchyBefore.Nodes, hierarchyAfter.Nodes);
        Assert.Equal(hierarchyBefore.Note, hierarchyAfter.Note);
        Assert.Equal(inputBefore, CapturePreparedOwnerInputs(inputs));
        Assert.All(
            inputs,
            input => Assert.Equal(
                EntityState.Detached,
                context.Entry(input)
                    .State));

        Assert.Equal("sentinel", graph.Sentinel.Entity.Value);
        Assert.Equal(EntityState.Unchanged, graph.Sentinel.State);
        Assert.Equal(0, hierarchyAfter.Issues);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Restored managed writes persist on ordinary retry without retaining failed forest inputs.</summary>
    [Fact]
    public async Task SaveChangesAsync_ComplexCallbackManagedRollback_AllowsExplicitWriteRetry()
    {
        // Arrange
        await using var database = await CreateComplexCallbackDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var callback = new SaveBoundaryCallback();
        ComplexCallbackRow? callbackRow = null;
        var failure = new ComplexCallbackCompletionFailure(() => ChangeComplexCallbackRow(
            callbackRow ?? throw new InvalidOperationException("The callback row was not loaded."),
            "nested-clear"));

        await using var context = await CreateComplexCallbackContextAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken,
            callback,
            failure);

        var graph = await LoadComplexCallbackGraphAsync(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

        callbackRow = graph.Row.Entity;
        var inputs = Enumerable
            .Range(101, 4)
            .Select(id => new NavigationNode
            {
                Id = id,
                Name = "Input " + id.ToString(CultureInfo.InvariantCulture),
            })
            .ToArray();

        var inputBefore = CapturePreparedOwnerInputs(inputs);
        var clrBefore = CaptureComplexCallbackClr(graph.Row.Entity);
        var nativeBefore = CaptureComplexCallbackNative(graph);
        var storedBefore = await ReadComplexCallbackRowAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var hierarchyBefore = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        callback.OnSaving = _ =>
        {
            callback.OnSaving = null;
            graph.Row.Property(value => value.Label)
                .CurrentValue = "explicit callback row";
        };

        // WHY: The insertion failure leaves its explicit ordinary callback write pending for a normal save.
        var firstError = await Record.ExceptionAsync(() => InsertComplexCallbackInputsAsync(
            context,
            "forest",
            inputs,
            cancellationToken: TestContext.Current.CancellationToken));

        var clrAfterFailure = CaptureComplexCallbackClr(graph.Row.Entity);
        var nativeAfterFailure = CaptureComplexCallbackNative(graph);
        var storedAfterFailure = await ReadComplexCallbackRowAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var retryError = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var storedAfter = await ReadComplexCallbackRowAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var hierarchyAfter = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(failure.Error, firstError);
        AssertComplexCallbackClr(clrBefore, clrAfterFailure);
        AssertComplexCallbackNative(nativeBefore, nativeAfterFailure);
        Assert.Equal(storedBefore, storedAfterFailure);
        Assert.Null(retryError);
        Assert.Equal(storedBefore with { Label = "explicit callback row" }, storedAfter);
        AssertComplexCallbackClr(clrBefore, CaptureComplexCallbackClr(graph.Row.Entity));
        Assert.Equal(EntityState.Unchanged, graph.Row.State);
        Assert.All(graph.Row.Properties, property => Assert.False(property.IsModified));
        Assert.Equal(hierarchyBefore.Nodes, hierarchyAfter.Nodes);
        Assert.Equal(hierarchyBefore.Note, hierarchyAfter.Note);
        Assert.Equal(inputBefore, CapturePreparedOwnerInputs(inputs));
        Assert.All(
            inputs,
            input => Assert.Equal(
                EntityState.Detached,
                context.Entry(input)
                    .State));

        Assert.Equal("sentinel", graph.Sentinel.Entity.Value);
        Assert.Equal(EntityState.Unchanged, graph.Sentinel.State);
        Assert.Equal(0, hierarchyAfter.Issues);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }
}

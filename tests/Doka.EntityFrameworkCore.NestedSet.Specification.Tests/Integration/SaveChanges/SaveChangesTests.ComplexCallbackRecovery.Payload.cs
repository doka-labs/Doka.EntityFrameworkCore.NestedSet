namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Ordinary rows recover captured nested topology after callback changes and actual SQL failure.</summary>
    [Theory]
    [InlineData("remove", false, false)]
    [InlineData("remove", true, false)]
    [InlineData("nested-clear", false, false)]
    [InlineData("nested-clear", true, false)]
    [InlineData("list-null", false, false)]
    [InlineData("list-null", true, false)]
    [InlineData("reference-null", false, false)]
    [InlineData("reference-null", true, false)]
    [InlineData("replace", false, false)]
    [InlineData("replace", true, false)]
    [InlineData("nested-null", false, false)]
    [InlineData("address-nested-clear", true, false)]
    [InlineData("edit", false, false)]
    [InlineData("add", true, false)]
    [InlineData("remove", false, true)]
    [InlineData("nested-clear", true, true)]
    public async Task SaveChangesAsync_ComplexCallbackRollback_RestoresOrdinaryRow(
        string mutation,
        bool automaticDetection,
        bool concurrency
    )
    {
        // Arrange
        await using var database = await CreateComplexCallbackDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var callback = new SaveBoundaryCallback();
        var failure = new PlannedParentFailure();
        IInterceptor[] interceptors = concurrency
            ? [callback]
            : [callback, failure];

        await using var context = await CreateComplexCallbackContextAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken,
            interceptors);

        var graph = await LoadComplexCallbackGraphAsync(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

        await PrepareComplexCallbackOrderingAsync(context, cancellationToken: TestContext.Current.CancellationToken);

        if (concurrency)
        {
            // WHY: The stale token belongs to an ordinary row, so hierarchy token policy cannot reject setup.
            graph.Row.Property(value => value.Label)
                .CurrentValue = "explicit row";

            await ChangeComplexCallbackVersionAsync(database, cancellationToken: TestContext.Current.CancellationToken);
        }

        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        var before = CapturePlannedParentState(context);
        var clrBefore = CaptureComplexCallbackClr(graph.Row.Entity);
        var nativeBefore = CaptureComplexCallbackNative(graph);
        var storedBefore = await ReadComplexCallbackRowAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var hierarchyBefore = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        callback.OnSaving = current =>
        {
            callback.OnSaving = null;
            ChangeComplexCallbackRow(graph.Row.Entity, mutation);

            if (concurrency && !automaticDetection)
            {
                // WHY: This consumer explicitly synchronizes deleted JSON ordinals to reach the SQL conflict.
                // Separate completion-failure controls retain undetected raw callback removal coverage.
                current.ChangeTracker.DetectChanges();
            }
        };

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var storedAfter = await ReadComplexCallbackRowAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var hierarchyAfter = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        if (concurrency)
        {
            Assert.True(
                error?.GetType() == typeof(DbUpdateConcurrencyException),
                error?.ToString() ?? "The expected database concurrency failure was not reported.");

            Assert.IsType<DbUpdateConcurrencyException>(error);
        }
        else
        {
            Assert.Same(failure.Error, error);
        }

        AssertPlannedParentState(before, CapturePlannedParentState(context));
        AssertComplexCallbackClr(clrBefore, CaptureComplexCallbackClr(graph.Row.Entity));
        AssertComplexCallbackNative(nativeBefore, CaptureComplexCallbackNative(graph));
        Assert.Equal(storedBefore, storedAfter);
        Assert.Equal(hierarchyBefore.Nodes, hierarchyAfter.Nodes);
        Assert.Equal(hierarchyBefore.Note, hierarchyAfter.Note);
        Assert.Equal("sentinel", graph.Sentinel.Entity.Value);
        Assert.Equal(EntityState.Unchanged, graph.Sentinel.State);
        Assert.Equal(0, hierarchyAfter.Issues);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Successful callbacks retain CLR topology and honor the caller's native detection setting.</summary>
    [Theory]
    [InlineData("remove", true)]
    [InlineData("reference-null", false)]
    public async Task SaveChangesAsync_ComplexCallbackSuccess_RetainsConsumerChanges(
        string mutation,
        bool automaticDetection
    )
    {
        // Arrange
        await using var database = await CreateComplexCallbackDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var callback = new SaveBoundaryCallback();
        await using var context = await CreateComplexCallbackContextAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken,
            callback);

        var graph = await LoadComplexCallbackGraphAsync(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

        await PrepareComplexCallbackOrderingAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        ComplexCallbackClrState? pending = null;
        var storedBefore = await ReadComplexCallbackRowAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        callback.OnSaving = _ =>
        {
            callback.OnSaving = null;
            ChangeComplexCallbackRow(graph.Row.Entity, mutation);
            pending = CaptureComplexCallbackClr(graph.Row.Entity);
        };

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var storedAfter = await ReadComplexCallbackRowAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var hierarchyAfter = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(error);
        Assert.NotNull(pending);
        AssertComplexCallbackClr(pending, CaptureComplexCallbackClr(graph.Row.Entity));
        Assert.Equal(
            "Zulu",
            hierarchyAfter.Nodes.Single(node => node.Id == 2)
                .Name);

        Assert.Equal(
            automaticDetection
                ? "first:part one,part two"
                : storedBefore.Items,
            storedAfter.Items);

        Assert.Equal(storedBefore.Address, storedAfter.Address);
        Assert.Equal(storedBefore.Tags, storedAfter.Tags);
        Assert.Equal("sentinel", hierarchyAfter.Note);
        Assert.Equal(0, hierarchyAfter.Issues);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }
}

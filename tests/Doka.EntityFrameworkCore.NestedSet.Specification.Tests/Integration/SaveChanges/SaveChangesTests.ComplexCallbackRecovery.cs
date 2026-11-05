namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Failed coordination restores complex containers changed after the pre-save snapshot.</summary>
    [Theory]
    [InlineData("owned", "remove", false)]
    [InlineData("split", "remove", true)]
    [InlineData("owned", "null", true)]
    [InlineData("split", "null", false)]
    [InlineData("owned", "collection-null", false)]
    [InlineData("split", "collection-null", true)]
    [InlineData("owned", "replace", true)]
    [InlineData("split", "edit", false)]
    [InlineData("owned", "add", true)]
    public async Task ComplexCallbackRollbackRestoresCapturedPeer(
        string shape,
        string mutation,
        bool automaticDetection
    )
    {
        // Arrange
        await using var database = await CreateComplexPeerRecoveryDatabaseAsync(shape,
            cancellationToken: TestContext.Current.CancellationToken);

        var callback = new SaveBoundaryCallback();
        var failure = new PlannedParentFailure();
        await using var context = await CreateComplexPeerRecoveryContextAsync(database, shape,
            cancellationToken: TestContext.Current.CancellationToken,
            callback, failure);

        var graph = await PrepareComplexPeerRecoveryAsync(context,
            cancellationToken: TestContext.Current.CancellationToken);

        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        var before = CapturePlannedParentState(context);
        var complexBefore = CaptureComplexPeerState(graph);
        var storedBefore = await ReadComplexPeerStoreAsync(database, shape,
            cancellationToken: TestContext.Current.CancellationToken);

        callback.OnSaving = _ =>
        {
            callback.OnSaving = null;
            if (mutation == "collection-null")
            {
                // WHY: Raw CLR assignment may remove a captured container even without automatic detection.
                graph.Peer.Entity.Items = null!;
            }
            else
            {
                ChangeComplexPeerPayload(graph, mutation);
            }
        };

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var storedAfter = await ReadComplexPeerStoreAsync(database, shape,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(failure.Error, error);
        AssertPlannedParentState(before, CapturePlannedParentState(context));
        AssertComplexPeerState(complexBefore, CaptureComplexPeerState(graph));
        Assert.Equal(storedBefore.Rows, storedAfter.Rows);
        Assert.Equal(0, storedAfter.Issues);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }

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
    public async Task ComplexCallbackRollbackRestoresOrdinaryRow(
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
        IInterceptor[] interceptors = concurrency ? [callback] : [callback, failure];
        await using var context = await CreateComplexCallbackContextAsync(database,
            cancellationToken: TestContext.Current.CancellationToken,
            interceptors);

        var graph = await LoadComplexCallbackGraphAsync(context,
            cancellationToken: TestContext.Current.CancellationToken);

        await PrepareComplexCallbackOrderingAsync(context, cancellationToken: TestContext.Current.CancellationToken);

        if (concurrency)
        {
            // WHY: The stale token belongs to an ordinary row, so hierarchy token policy cannot reject setup.
            graph.Row.Property(value => value.Label).CurrentValue = "explicit row";
            await ChangeComplexCallbackVersionAsync(database, cancellationToken: TestContext.Current.CancellationToken);
        }

        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        var before = CapturePlannedParentState(context);
        var clrBefore = CaptureComplexCallbackClr(graph.Row.Entity);
        var nativeBefore = CaptureComplexCallbackNative(graph);
        var storedBefore = await ReadComplexCallbackRowAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        var hierarchyBefore = await ReadPreparedOwnerStoreAsync(database,
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
        var storedAfter = await ReadComplexCallbackRowAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        var hierarchyAfter = await ReadPreparedOwnerStoreAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        if (concurrency)
        {
            Assert.True(error?.GetType() == typeof(DbUpdateConcurrencyException),
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

    /// <summary>A recovered complex callback failure permits the same coordinated save without graph repair.</summary>
    [Theory]
    [InlineData("remove", true)]
    [InlineData("nested-clear", false)]
    [InlineData("reference-null", true)]
    public async Task ComplexCallbackRollbackAllowsIdenticalRetry(string mutation, bool automaticDetection)
    {
        // Arrange
        await using var database = await CreateComplexCallbackDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var callback = new SaveBoundaryCallback();
        var failure = new PlannedParentFailure();
        await using var context = await CreateComplexCallbackContextAsync(database,
            cancellationToken: TestContext.Current.CancellationToken,
            callback, failure);

        var graph = await LoadComplexCallbackGraphAsync(context,
            cancellationToken: TestContext.Current.CancellationToken);

        await PrepareComplexCallbackOrderingAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        var before = CapturePlannedParentState(context);
        var clrBefore = CaptureComplexCallbackClr(graph.Row.Entity);
        var nativeBefore = CaptureComplexCallbackNative(graph);
        var storedBefore = await ReadComplexCallbackRowAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        callback.OnSaving = _ =>
        {
            callback.OnSaving = null;
            ChangeComplexCallbackRow(graph.Row.Entity, mutation);
        };

        // WHY: The first one-shot failure establishes retry state; no caller repair is allowed between saves.
        var firstError = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var afterFailure = CapturePlannedParentState(context);
        var clrAfterFailure = CaptureComplexCallbackClr(graph.Row.Entity);
        var nativeAfterFailure = CaptureComplexCallbackNative(graph);
        var storedAfterFailure = await ReadComplexCallbackRowAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var retryError = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var storedAfter = await ReadComplexCallbackRowAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        var hierarchyAfter = await ReadPreparedOwnerStoreAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(failure.Error, firstError);
        AssertPlannedParentState(before, afterFailure);
        AssertComplexCallbackClr(clrBefore, clrAfterFailure);
        AssertComplexCallbackNative(nativeBefore, nativeAfterFailure);
        Assert.Equal(storedBefore, storedAfterFailure);
        Assert.Null(retryError);
        AssertComplexCallbackClr(clrBefore, CaptureComplexCallbackClr(graph.Row.Entity));
        Assert.Equal(storedBefore, storedAfter);
        Assert.Equal("Zulu", hierarchyAfter.Nodes.Single(node => node.Id == 2).Name);
        Assert.Equal("sentinel", hierarchyAfter.Note);
        Assert.Equal(0, hierarchyAfter.Issues);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Successful callbacks retain CLR topology and honor the caller's native detection setting.</summary>
    [Theory]
    [InlineData("remove", true)]
    [InlineData("reference-null", false)]
    public async Task ComplexCallbackSuccessRetainsConsumerChanges(string mutation, bool automaticDetection)
    {
        // Arrange
        await using var database = await CreateComplexCallbackDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var callback = new SaveBoundaryCallback();
        await using var context = await CreateComplexCallbackContextAsync(database,
            cancellationToken: TestContext.Current.CancellationToken,
            callback);

        var graph = await LoadComplexCallbackGraphAsync(context,
            cancellationToken: TestContext.Current.CancellationToken);

        await PrepareComplexCallbackOrderingAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        ComplexCallbackClrState? pending = null;
        var storedBefore = await ReadComplexCallbackRowAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        callback.OnSaving = _ =>
        {
            callback.OnSaving = null;
            ChangeComplexCallbackRow(graph.Row.Entity, mutation);
            pending = CaptureComplexCallbackClr(graph.Row.Entity);
        };

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var storedAfter = await ReadComplexCallbackRowAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        var hierarchyAfter = await ReadPreparedOwnerStoreAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(error);
        Assert.NotNull(pending);
        AssertComplexCallbackClr(pending, CaptureComplexCallbackClr(graph.Row.Entity));
        Assert.Equal("Zulu", hierarchyAfter.Nodes.Single(node => node.Id == 2).Name);
        Assert.Equal(automaticDetection ? "first:part one,part two" : storedBefore.Items, storedAfter.Items);
        Assert.Equal(storedBefore.Address, storedAfter.Address);
        Assert.Equal(storedBefore.Tags, storedAfter.Tags);
        Assert.Equal("sentinel", hierarchyAfter.Note);
        Assert.Equal(0, hierarchyAfter.Issues);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Managed recovery restores ordinary complex writes changed after the managed snapshot.</summary>
    [Theory]
    [InlineData("single", "remove", false)]
    [InlineData("subtree", "nested-clear", true)]
    [InlineData("forest", "list-null", false)]
    [InlineData("forest", "reference-null", true)]
    public async Task ComplexCallbackManagedCompletionRestoresPendingWrite(
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
            callbackRow ?? throw new InvalidOperationException("The callback row was not loaded."), mutation));

        await using var context = await CreateComplexCallbackContextAsync(database,
            cancellationToken: TestContext.Current.CancellationToken,
            callback, failure);

        var graph = await LoadComplexCallbackGraphAsync(context,
            cancellationToken: TestContext.Current.CancellationToken);

        callbackRow = graph.Row.Entity;
        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        var inputs = Enumerable.Range(101, shape == "single" ? 1 : shape == "subtree" ? 2 : 4)
            .Select(id => new NavigationNode { Id = id, Name = "Input " + id.ToString(CultureInfo.InvariantCulture) })
            .ToArray();

        var inputBefore = CapturePreparedOwnerInputs(inputs);
        var clrBefore = CaptureComplexCallbackClr(graph.Row.Entity);
        var nativeBefore = CaptureComplexCallbackNative(graph);
        var storedBefore = await ReadComplexCallbackRowAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        var hierarchyBefore = await ReadPreparedOwnerStoreAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        PlannedEntryState[]? pending = null;
        callback.OnSaving = current =>
        {
            callback.OnSaving = null;
            graph.Row.Property(value => value.Label).CurrentValue = "explicit callback row";
            // WHY: SavingChanges writes belong to the managed baseline. The destructive mutation occurs later,
            // in SavedChanges, after this exact pending ordinary write has been captured by the library.
            pending = CapturePlannedParentState(current).Where(entry => ReferenceEquals(entry.Entity, callbackRow)
                || ReferenceEquals(entry.Entity, graph.Sentinel.Entity)).ToArray();
        };

        // Act
        var error = await Record.ExceptionAsync(() => InsertComplexCallbackInputsAsync(context, shape, inputs,
            cancellationToken: TestContext.Current.CancellationToken));

        var storedAfter = await ReadComplexCallbackRowAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        var hierarchyAfter = await ReadPreparedOwnerStoreAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(failure.Error, error);
        Assert.NotNull(pending);
        AssertPlannedParentState(pending, CapturePlannedParentState(context).Where(entry =>
            ReferenceEquals(entry.Entity, callbackRow) || ReferenceEquals(entry.Entity, graph.Sentinel.Entity))
            .ToArray());

        AssertComplexCallbackClr(clrBefore, CaptureComplexCallbackClr(graph.Row.Entity));
        AssertComplexCallbackNative(nativeBefore, CaptureComplexCallbackNative(graph));
        Assert.Equal(storedBefore, storedAfter);
        Assert.Equal(hierarchyBefore.Nodes, hierarchyAfter.Nodes);
        Assert.Equal(hierarchyBefore.Note, hierarchyAfter.Note);
        Assert.Equal(inputBefore, CapturePreparedOwnerInputs(inputs));
        Assert.All(inputs, input => Assert.Equal(EntityState.Detached, context.Entry(input).State));
        Assert.Equal("sentinel", graph.Sentinel.Entity.Value);
        Assert.Equal(EntityState.Unchanged, graph.Sentinel.State);
        Assert.Equal(0, hierarchyAfter.Issues);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Callback recovery preserves raw values and list membership already pending before the save.</summary>
    [Fact]
    public async Task ComplexCallbackRollbackRetainsEarlierPendingGraph()
    {
        // Arrange
        await using var database = await CreateComplexCallbackDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var callback = new SaveBoundaryCallback();
        var failure = new PlannedParentFailure();
        await using var context = await CreateComplexCallbackContextAsync(database,
            cancellationToken: TestContext.Current.CancellationToken,
            callback, failure);

        var graph = await LoadComplexCallbackGraphAsync(context,
            cancellationToken: TestContext.Current.CancellationToken);

        await PrepareComplexCallbackOrderingAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        graph.Row.Entity.Items![0].Text = "pending before save";
        graph.Row.Entity.Items.RemoveAt(1);
        var before = CapturePlannedParentState(context);
        var clrBefore = CaptureComplexCallbackClr(graph.Row.Entity);
        var nativeBefore = CaptureComplexCallbackNative(graph);
        var storedBefore = await ReadComplexCallbackRowAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        var hierarchyBefore = await ReadPreparedOwnerStoreAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        callback.OnSaving = _ =>
        {
            callback.OnSaving = null;
            ChangeComplexCallbackRow(graph.Row.Entity, "remove");
        };

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var storedAfter = await ReadComplexCallbackRowAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        var hierarchyAfter = await ReadPreparedOwnerStoreAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(failure.Error, error);
        AssertPlannedParentState(before, CapturePlannedParentState(context));
        AssertComplexCallbackClr(clrBefore, CaptureComplexCallbackClr(graph.Row.Entity));
        AssertComplexCallbackNative(nativeBefore, CaptureComplexCallbackNative(graph));
        Assert.Equal(storedBefore, storedAfter);
        Assert.Equal(hierarchyBefore.Nodes, hierarchyAfter.Nodes);
        Assert.Equal(hierarchyBefore.Note, hierarchyAfter.Note);
        Assert.Equal(0, hierarchyAfter.Issues);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Restored managed writes persist on ordinary retry without retaining failed forest inputs.</summary>
    [Fact]
    public async Task ComplexCallbackManagedRollbackAllowsExplicitWriteRetry()
    {
        // Arrange
        await using var database = await CreateComplexCallbackDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var callback = new SaveBoundaryCallback();
        ComplexCallbackRow? callbackRow = null;
        var failure = new ComplexCallbackCompletionFailure(() => ChangeComplexCallbackRow(
            callbackRow ?? throw new InvalidOperationException("The callback row was not loaded."), "nested-clear"));

        await using var context = await CreateComplexCallbackContextAsync(database,
            cancellationToken: TestContext.Current.CancellationToken,
            callback, failure);

        var graph = await LoadComplexCallbackGraphAsync(context,
            cancellationToken: TestContext.Current.CancellationToken);

        callbackRow = graph.Row.Entity;
        var inputs = Enumerable.Range(101, 4).Select(id => new NavigationNode
            { Id = id, Name = "Input " + id.ToString(CultureInfo.InvariantCulture) }).ToArray();

        var inputBefore = CapturePreparedOwnerInputs(inputs);
        var clrBefore = CaptureComplexCallbackClr(graph.Row.Entity);
        var nativeBefore = CaptureComplexCallbackNative(graph);
        var storedBefore = await ReadComplexCallbackRowAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        var hierarchyBefore = await ReadPreparedOwnerStoreAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        callback.OnSaving = _ =>
        {
            callback.OnSaving = null;
            graph.Row.Property(value => value.Label).CurrentValue = "explicit callback row";
        };

        // WHY: The insertion failure leaves its explicit ordinary callback write pending for a normal save.
        var firstError = await Record.ExceptionAsync(() => InsertComplexCallbackInputsAsync(context, "forest", inputs,
            cancellationToken: TestContext.Current.CancellationToken));

        var clrAfterFailure = CaptureComplexCallbackClr(graph.Row.Entity);
        var nativeAfterFailure = CaptureComplexCallbackNative(graph);
        var storedAfterFailure = await ReadComplexCallbackRowAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var retryError = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var storedAfter = await ReadComplexCallbackRowAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        var hierarchyAfter = await ReadPreparedOwnerStoreAsync(database,
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
        Assert.All(inputs, input => Assert.Equal(EntityState.Detached, context.Entry(input).State));
        Assert.Equal("sentinel", graph.Sentinel.Entity.Value);
        Assert.Equal(EntityState.Unchanged, graph.Sentinel.State);
        Assert.Equal(0, hierarchyAfter.Issues);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }
}

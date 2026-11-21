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
    public async Task SaveChangesAsync_ComplexCallbackRollback_RestoresCapturedPeer(
        string shape,
        string mutation,
        bool automaticDetection
    )
    {
        // Arrange
        await using var database = await CreateComplexPeerRecoveryDatabaseAsync(
            shape,
            cancellationToken: TestContext.Current.CancellationToken);

        var callback = new SaveBoundaryCallback();
        var failure = new PlannedParentFailure();
        await using var context = await CreateComplexPeerRecoveryContextAsync(
            database,
            shape,
            cancellationToken: TestContext.Current.CancellationToken,
            callback,
            failure);

        var graph = await PrepareComplexPeerRecoveryAsync(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        var before = CapturePlannedParentState(context);
        var complexBefore = CaptureComplexPeerState(graph);
        var storedBefore = await ReadComplexPeerStoreAsync(
            database,
            shape,
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
        var storedAfter = await ReadComplexPeerStoreAsync(
            database,
            shape,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(failure.Error, error);
        AssertPlannedParentState(before, CapturePlannedParentState(context));
        AssertComplexPeerState(complexBefore, CaptureComplexPeerState(graph));
        Assert.Equal(storedBefore.Rows, storedAfter.Rows);
        Assert.Equal(0, storedAfter.Issues);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>A recovered complex callback failure permits the same coordinated save without graph repair.</summary>
    [Theory]
    [InlineData("remove", true)]
    [InlineData("nested-clear", false)]
    [InlineData("reference-null", true)]
    public async Task SaveChangesAsync_ComplexCallbackRollback_AllowsIdenticalRetry(
        string mutation,
        bool automaticDetection
    )
    {
        // Arrange
        await using var database = await CreateComplexCallbackDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var callback = new SaveBoundaryCallback();
        var failure = new PlannedParentFailure();
        await using var context = await CreateComplexCallbackContextAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken,
            callback,
            failure);

        var graph = await LoadComplexCallbackGraphAsync(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

        await PrepareComplexCallbackOrderingAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        var before = CapturePlannedParentState(context);
        var clrBefore = CaptureComplexCallbackClr(graph.Row.Entity);
        var nativeBefore = CaptureComplexCallbackNative(graph);
        var storedBefore = await ReadComplexCallbackRowAsync(
            database,
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
        AssertPlannedParentState(before, afterFailure);
        AssertComplexCallbackClr(clrBefore, clrAfterFailure);
        AssertComplexCallbackNative(nativeBefore, nativeAfterFailure);
        Assert.Equal(storedBefore, storedAfterFailure);
        Assert.Null(retryError);
        AssertComplexCallbackClr(clrBefore, CaptureComplexCallbackClr(graph.Row.Entity));
        Assert.Equal(storedBefore, storedAfter);
        Assert.Equal(
            "Zulu",
            hierarchyAfter.Nodes.Single(node => node.Id == 2)
                .Name);

        Assert.Equal("sentinel", hierarchyAfter.Note);
        Assert.Equal(0, hierarchyAfter.Issues);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Callback recovery preserves raw values and list membership already pending before the save.</summary>
    [Fact]
    public async Task SaveChangesAsync_ComplexCallbackRollback_RetainsEarlierPendingGraph()
    {
        // Arrange
        await using var database = await CreateComplexCallbackDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var callback = new SaveBoundaryCallback();
        var failure = new PlannedParentFailure();
        await using var context = await CreateComplexCallbackContextAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken,
            callback,
            failure);

        var graph = await LoadComplexCallbackGraphAsync(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

        await PrepareComplexCallbackOrderingAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        graph.Row.Entity.Items![0].Text = "pending before save";
        graph.Row.Entity.Items.RemoveAt(1);
        var before = CapturePlannedParentState(context);
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
            ChangeComplexCallbackRow(graph.Row.Entity, "remove");
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
}

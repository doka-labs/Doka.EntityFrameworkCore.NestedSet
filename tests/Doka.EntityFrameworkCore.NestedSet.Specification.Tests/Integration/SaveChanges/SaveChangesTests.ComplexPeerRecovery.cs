namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Recovery preserves unmarked complex containers and leaves on unchanged physical-row peers.</summary>
    [Theory]
    [InlineData("owned", "edit", false)]
    [InlineData("owned", "add", false)]
    [InlineData("owned", "remove", true)]
    [InlineData("owned", "replace", true)]
    [InlineData("owned", "null", false)]
    [InlineData("split", "edit", true)]
    [InlineData("split", "add", false)]
    [InlineData("split", "remove", false)]
    [InlineData("split", "replace", false)]
    [InlineData("split", "null", true)]
    [InlineData("separate", "edit", false)]
    [InlineData("separate", "null", false)]
    [InlineData("owned", "address-edit", false)]
    [InlineData("split", "address-replace", false)]
    public async Task ComplexPeerRollbackPreservesPendingGraph(string shape, string mutation, bool hierarchyFailure)
    {
        // Arrange
        await using var database = await CreateComplexPeerRecoveryDatabaseAsync(shape,
            cancellationToken: TestContext.Current.CancellationToken);

        var (failure, expectedError) = CreatePayloadOwnerRecoveryFailure(hierarchyFailure);
        await using var context = await CreateComplexPeerRecoveryContextAsync(database, shape,
            cancellationToken: TestContext.Current.CancellationToken,
            failure);

        ArmPayloadOwnerHierarchyFailure(context, failure);
        var graph = await PrepareComplexPeerRecoveryAsync(context,
            cancellationToken: TestContext.Current.CancellationToken);

        ChangeComplexPeerPayload(graph, mutation);
        var before = CapturePlannedParentState(context);
        var complexBefore = CaptureComplexPeerState(graph);
        var storedBefore = await ReadComplexPeerStoreAsync(database, shape,
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var after = CapturePlannedParentState(context);
        var complexAfter = CaptureComplexPeerState(graph);
        var storedAfter = await ReadComplexPeerStoreAsync(database, shape,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        AssertPlannedParentState(before, after);
        AssertComplexPeerState(complexBefore, complexAfter);
        Assert.Same(expectedError, error);
        Assert.Equal(EntityState.Unchanged, graph.Peer.State);
        Assert.Equal(storedBefore.Rows, storedAfter.Rows);
        Assert.Equal(0, storedAfter.Issues);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Identical retry preserves the caller graph and EF's native manual-detection write semantics.</summary>
    [Theory]
    [InlineData("owned", "add", false)]
    [InlineData("split", "replace", true)]
    [InlineData("owned", "null", true)]
    public async Task ComplexPeerRollbackAllowsIdenticalRetry(string shape, string mutation, bool hierarchyFailure)
    {
        // Arrange
        await using var database = await CreateComplexPeerRecoveryDatabaseAsync(shape,
            cancellationToken: TestContext.Current.CancellationToken);

        var (failure, expectedError) = CreatePayloadOwnerRecoveryFailure(hierarchyFailure);
        await using var context = await CreateComplexPeerRecoveryContextAsync(database, shape,
            cancellationToken: TestContext.Current.CancellationToken,
            failure);

        ArmPayloadOwnerHierarchyFailure(context, failure);
        var graph = await PrepareComplexPeerRecoveryAsync(context,
            cancellationToken: TestContext.Current.CancellationToken);

        ChangeComplexPeerPayload(graph, mutation);
        var before = CapturePlannedParentState(context);
        var complexBefore = CaptureComplexPeerState(graph);
        var storedBefore = await ReadComplexPeerStoreAsync(database, shape,
            cancellationToken: TestContext.Current.CancellationToken);

        var detections = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detections++;
        // WHY: The one-shot failure is the retry's precondition; no state or modification flag is repaired manually.
        var firstError = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var afterFailure = CapturePlannedParentState(context);
        var complexAfterFailure = CaptureComplexPeerState(graph);

        // Act
        var retryError = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var storedAfter = await ReadComplexPeerStoreAsync(database, shape,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        AssertPlannedParentState(before, afterFailure);
        AssertComplexPeerState(complexBefore, complexAfterFailure);
        Assert.Same(expectedError, firstError);
        Assert.Null(retryError);
        AssertComplexPeerCallerValues(complexBefore, graph);
        Assert.Equal(EntityState.Unchanged, graph.Peer.State);
        Assert.Equal("Zulu", storedAfter.Rows.Single(row => row.Id == 2).Name);
        // WHY: Native EF preparation promotes unmatched raw JSON ordinals to Added. The pure EF
        // regression distinguishes that cardinality rule from unmarked existing scalar values.
        Assert.Equal(storedBefore.Rows.Select(row => (row.Id, row.Address,
                Items: row.Id == 2 && mutation == "add" ? row.Items + "|raw added" : row.Items)),
            storedAfter.Rows.Select(row => (row.Id, row.Address, row.Items)));

        Assert.Equal(0, storedAfter.Issues);
        Assert.Equal(0, detections);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Successful shared-row saves retain raw identities and match EF's native JSON write behavior.</summary>
    [Theory]
    [InlineData("owned", "edit")]
    [InlineData("owned", "add")]
    [InlineData("split", "replace")]
    [InlineData("split", "add")]
    [InlineData("owned", "null")]
    [InlineData("separate", "remove")]
    public async Task ComplexPeerSuccessPreservesUnmarkedPayload(string shape, string mutation)
    {
        // Arrange
        await using var database = await CreateComplexPeerRecoveryDatabaseAsync(shape,
            cancellationToken: TestContext.Current.CancellationToken);

        await using var context = await CreateComplexPeerRecoveryContextAsync(database, shape,
            cancellationToken: TestContext.Current.CancellationToken);

        var graph = await PrepareComplexPeerRecoveryAsync(context,
            cancellationToken: TestContext.Current.CancellationToken);

        ChangeComplexPeerPayload(graph, mutation);
        var complexBefore = CaptureComplexPeerState(graph);
        var storedBefore = await ReadComplexPeerStoreAsync(database, shape,
            cancellationToken: TestContext.Current.CancellationToken);

        var detections = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detections++;

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var storedAfter = await ReadComplexPeerStoreAsync(database, shape,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(error);
        AssertComplexPeerCallerValues(complexBefore, graph);
        Assert.Equal(EntityState.Unchanged, graph.Peer.State);
        Assert.All(graph.Peer.Properties, property => Assert.False(property.IsModified));
        Assert.False(graph.Peer.ComplexCollection(value => value.Items).IsModified);
        Assert.False(graph.Peer.ComplexProperty(value => value.Address).IsModified);
        Assert.Equal("Zulu", storedAfter.Rows.Single(row => row.Id == 2).Name);
        // WHY: EF creates Added JSON element entries for unmatched raw ordinals during shared-row
        // preparation, even without automatic detection. Existing unmarked scalar edits remain unsaved.
        Assert.Equal(storedBefore.Rows.Select(row => (row.Id, row.Address,
                Items: row.Id == 2 && mutation == "add" ? row.Items + "|raw added" : row.Items)),
            storedAfter.Rows.Select(row => (row.Id, row.Address, row.Items)));

        Assert.Equal(0, storedAfter.Issues);
        Assert.Equal(0, detections);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }
}

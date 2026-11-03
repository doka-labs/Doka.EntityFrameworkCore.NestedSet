using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Prepared collections retain every authorized input across bounded batches and tree plans.</summary>
    [Theory]
    [InlineData("subtree", 65)]
    [InlineData("subtree", 130)]
    [InlineData("forest", 130)]
    public async Task PreparedOwnerImportAcrossBatchesAllowsFollowingSave(string shape, int nodes)
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync();
        await SeedPreparedNavigationDestinationAsync(database);
        var callback = new SaveBoundaryCallback();
        await using var context = await CreateNavigationContextAsync(database, wrapped: true, callback);
        var graph = await PrepareOwnerImportAsync(context, nodes);
        var observedBatches = new List<int[]>();
        callback.OnSaving = current => observedBatches.Add(current.GetService<IUpdateAdapterFactory>().Create().Entries
            .Where(entry => entry.EntityType.ClrType == typeof(NavigationNode))
            .Select(entry => entry.GetCurrentValue<int>(entry.EntityType.FindProperty(nameof(NavigationNode.Id))!))
            .Order().ToArray());

        // Act
        await InsertPreparedOwnerImportAsync(context, shape, graph.Inputs, CancellationToken.None);
        callback.OnSaving = null;
        graph.NoteValue.CurrentValue = "following payload";
        await context.SaveChangesAsync(CancellationToken.None);
        var stored = await ReadPreparedOwnerStoreAsync(database);

        // Assert
        Assert.Equal(4 + nodes, stored.Nodes.Length);
        Assert.Equal("following payload", stored.Note);
        Assert.Equal(0, stored.Issues);
        Assert.Equal((nodes + 63) / 64, observedBatches.Count);
        Assert.All(observedBatches, batch => Assert.InRange(batch.Length, 1, 64));
        Assert.Equal(graph.Inputs.Select(input => input.Id).Order(),
            observedBatches.SelectMany(batch => batch).Order());
        Assert.Equal(graph.Inputs, graph.Owner.Nodes, ReferenceEqualityComparer.Instance);
        Assert.All(graph.Inputs, input => Assert.Equal(EntityState.Detached, context.Entry(input).State));
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>A second-batch failure restores the exact pre-import owner graph and detached inputs.</summary>
    [Theory]
    [InlineData("subtree", 65)]
    [InlineData("subtree", 130)]
    [InlineData("forest", 130)]
    public async Task PreparedOwnerImportLateFailureRestoresExactState(string shape, int nodes)
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync();
        await SeedPreparedNavigationDestinationAsync(database);
        var failure = new PreparedOwnerBatchFailure();
        await using var context = await CreateNavigationContextAsync(database, wrapped: true, failure);
        var graph = await PrepareOwnerImportAsync(context, nodes);
        var before = CapturePlannedParentState(context);
        var inputBefore = CapturePreparedOwnerInputs(graph.Inputs);
        var storedBefore = await ReadPreparedOwnerStoreAsync(database);

        // Act
        var error = await Record.ExceptionAsync(() =>
            InsertPreparedOwnerImportAsync(context, shape, graph.Inputs, CancellationToken.None));

        var after = CapturePlannedParentState(context);
        var inputAfter = CapturePreparedOwnerInputs(graph.Inputs);
        var storedAfter = await ReadPreparedOwnerStoreAsync(database);

        // Assert
        Assert.Same(failure.Error, error);
        Assert.Equal(2, failure.CompletedSaves);
        AssertPlannedParentState(before, after);
        Assert.Equal(inputBefore, inputAfter);
        Assert.All(graph.Inputs, input => Assert.Empty(input.Children));
        Assert.Equal(storedBefore.Nodes, storedAfter.Nodes);
        Assert.Equal(storedBefore.Note, storedAfter.Note);
        Assert.Equal(0, storedAfter.Issues);
        Assert.All(graph.Inputs, input => Assert.Equal(EntityState.Detached, context.Entry(input).State));
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>The same import retries without repairing its owner collection or structural input state.</summary>
    [Theory]
    [InlineData("subtree", 65)]
    [InlineData("subtree", 130)]
    [InlineData("forest", 130)]
    public async Task PreparedOwnerImportRetriesIdenticallyAfterLateFailure(string shape, int nodes)
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync();
        await SeedPreparedNavigationDestinationAsync(database);
        var failure = new PreparedOwnerBatchFailure();
        await using var context = await CreateNavigationContextAsync(database, wrapped: true, failure);
        var graph = await PrepareOwnerImportAsync(context, nodes);
        var before = CapturePlannedParentState(context);
        var inputBefore = CapturePreparedOwnerInputs(graph.Inputs);
        // WHY: The first rollback is the retry's precondition; this observer fails only its second save.
        var firstError = await Record.ExceptionAsync(() =>
            InsertPreparedOwnerImportAsync(context, shape, graph.Inputs, CancellationToken.None));

        var afterFailure = CapturePlannedParentState(context);
        var inputAfterFailure = CapturePreparedOwnerInputs(graph.Inputs);

        // Act
        await InsertPreparedOwnerImportAsync(context, shape, graph.Inputs, CancellationToken.None);
        var stored = await ReadPreparedOwnerStoreAsync(database);

        // Assert
        Assert.Same(failure.Error, firstError);
        AssertPlannedParentState(before, afterFailure);
        Assert.Equal(inputBefore, inputAfterFailure);
        Assert.Equal(4 + nodes, stored.Nodes.Length);
        Assert.Equal("original", stored.Note);
        Assert.Equal(0, stored.Issues);
        Assert.Equal(graph.Inputs, graph.Owner.Nodes, ReferenceEqualityComparer.Instance);
        Assert.All(graph.Inputs, input => Assert.Equal(EntityState.Detached, context.Entry(input).State));
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Cancellation between batches also restores inputs that have not entered their active batch.</summary>
    [Theory]
    [InlineData("subtree", 65)]
    [InlineData("subtree", 130)]
    [InlineData("forest", 130)]
    public async Task PreparedOwnerImportCancellationRestoresExactState(string shape, int nodes)
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync();
        await SeedPreparedNavigationDestinationAsync(database);
        using var cancellation = new CancellationTokenSource();
        var observer = new PreparedOwnerBatchCancellation(cancellation);
        await using var context = await CreateNavigationContextAsync(database, wrapped: true, observer);
        var graph = await PrepareOwnerImportAsync(context, nodes);
        var before = CapturePlannedParentState(context);
        var inputBefore = CapturePreparedOwnerInputs(graph.Inputs);
        var storedBefore = await ReadPreparedOwnerStoreAsync(database);

        // Act
        var error = await Record.ExceptionAsync(() =>
            InsertPreparedOwnerImportAsync(context, shape, graph.Inputs, cancellation.Token));

        var after = CapturePlannedParentState(context);
        var inputAfter = CapturePreparedOwnerInputs(graph.Inputs);
        var storedAfter = await ReadPreparedOwnerStoreAsync(database);

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.Equal(1, observer.CompletedSaves);
        AssertPlannedParentState(before, after);
        Assert.Equal(inputBefore, inputAfter);
        Assert.All(graph.Inputs, input => Assert.Empty(input.Children));
        Assert.Equal(storedBefore.Nodes, storedAfter.Nodes);
        Assert.Equal(storedBefore.Note, storedAfter.Note);
        Assert.Equal(0, storedAfter.Issues);
        Assert.All(graph.Inputs, input => Assert.Equal(EntityState.Detached, context.Entry(input).State));
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Future membership never permits unknown members, premature writes or another scope owner.</summary>
    [Theory]
    [InlineData("subtree", 65, "unknown")]
    [InlineData("subtree", 65, "added")]
    [InlineData("subtree", 65, "scope")]
    [InlineData("subtree", 130, "unknown")]
    [InlineData("subtree", 130, "added")]
    [InlineData("subtree", 130, "scope")]
    [InlineData("forest", 130, "unknown")]
    [InlineData("forest", 130, "added")]
    [InlineData("forest", 130, "scope")]
    public async Task PreparedOwnerImportRejectsUnauthorizedFutureMembers(string shape, int nodes, string change)
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync();
        await SeedPreparedNavigationDestinationAsync(database);
        var callback = new SaveBoundaryCallback();
        var probe = new NavigationWriteProbe();
        await using var context = await CreateNavigationContextAsync(database, wrapped: true, callback, probe);
        var graph = await PrepareOwnerImportAsync(context, nodes);
        var future = graph.Inputs[^1];

        if (change == "scope")
        {
            // WHY: Raw input scope is staging-owned; membership must use its bound plan scope instead.
            future.Scope = graph.OtherOwner.Id;
        }

        var before = CapturePlannedParentState(context);
        var inputBefore = CapturePreparedOwnerInputs(graph.Inputs);
        var storedBefore = await ReadPreparedOwnerStoreAsync(database);
        int? writesAtCallback = null;
        int? activeAtCallback = null;
        callback.OnSaving = current =>
        {
            writesAtCallback = probe.Writes;
            activeAtCallback = current.GetService<IUpdateAdapterFactory>().Create().Entries
                .Count(entry => entry.EntityType.ClrType == typeof(NavigationNode));

            switch (change)
            {
                case "unknown":
                    graph.Owner.Nodes.Add(new NavigationNode { Id = 999, Scope = graph.Owner.Id, Name = "Unknown" });
                    break;
                case "added":
                    // WHY: The first batch is authorized already; this explicitly registers a different future batch.
                    // ReSharper disable once MethodHasAsyncOverload
                    current.Add(future);
                    break;
                case "scope":
                    graph.OtherOwner.Nodes.Add(future);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(change));
            }
        };

        // Act
        var error = await Record.ExceptionAsync(() =>
            InsertPreparedOwnerImportAsync(context, shape, graph.Inputs, CancellationToken.None));

        var after = CapturePlannedParentState(context);
        var inputAfter = CapturePreparedOwnerInputs(graph.Inputs);
        var storedAfter = await ReadPreparedOwnerStoreAsync(database);

        // Assert
        var rejection = Assert.IsType<NestedSetException>(error);

        if (change == "added")
        {
            // WHY: An explicitly attached future node violates the existing active import-membership contract.
            Assert.Equal(NestedSetErrorCode.InvalidImport, rejection.Code);
            Assert.Equal("Save callbacks must not add or remove hierarchy entities during an import.",
                rejection.Message);
        }
        else
        {
            Assert.Equal(NestedSetErrorCode.InvalidContext, rejection.Code);
        }

        Assert.Equal(64, activeAtCallback);
        Assert.Equal(writesAtCallback, probe.Writes);
        AssertPlannedParentState(before, after);
        Assert.Equal(inputBefore, inputAfter);
        Assert.All(graph.Inputs, input => Assert.Empty(input.Children));
        Assert.Equal(storedBefore.Nodes, storedAfter.Nodes);
        Assert.Equal(storedBefore.Note, storedAfter.Note);
        Assert.Equal(0, storedAfter.Issues);
        Assert.All(graph.Inputs, input => Assert.Equal(EntityState.Detached, context.Entry(input).State));
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }
}

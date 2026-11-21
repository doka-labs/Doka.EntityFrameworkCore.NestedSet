using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Prepared collections retain every authorized input across bounded batches and tree plans.</summary>
    [Theory]
    [InlineData("subtree", 65)]
    [InlineData("subtree", 130)]
    [InlineData("forest", 130)]
    public async Task InsertPreparedOwnerImportAsync_PreparedOwnerImportAcrossBatches_AllowsFollowingSave(
        string shape,
        int nodes
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedPreparedNavigationDestinationAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var callback = new SaveBoundaryCallback();
        await using var context = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken,
            callback);

        var graph = await PrepareOwnerImportAsync(
            context,
            nodes,
            cancellationToken: TestContext.Current.CancellationToken);

        var observedBatches = new List<int[]>();
        callback.OnSaving = current => observedBatches.Add(
            current
                .GetService<IUpdateAdapterFactory>()
                .Create()
                .Entries
                .Where(entry => entry.EntityType.ClrType == typeof(NavigationNode))
                .Select(entry => entry.GetCurrentValue<int>(entry.EntityType.FindProperty(nameof(NavigationNode.Id))!))
                .Order()
                .ToArray());

        // Act
        await InsertPreparedOwnerImportAsync(context, shape, graph.Inputs, CancellationToken.None);
        callback.OnSaving = null;
        graph.NoteValue.CurrentValue = "following payload";
        await context.SaveChangesAsync(CancellationToken.None);
        var stored = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(4 + nodes, stored.Nodes.Length);
        Assert.Equal("following payload", stored.Note);
        Assert.Equal(0, stored.Issues);
        Assert.Equal((nodes + 63) / 64, observedBatches.Count);
        Assert.All(observedBatches, batch => Assert.InRange(batch.Length, 1, 64));
        Assert.Equal(
            graph
                .Inputs
                .Select(input => input.Id)
                .Order(),
            observedBatches
                .SelectMany(batch => batch)
                .Order());

        Assert.Equal(graph.Inputs, graph.Owner.Nodes, ReferenceEqualityComparer.Instance);
        Assert.All(
            graph.Inputs,
            input => Assert.Equal(
                EntityState.Detached,
                context.Entry(input)
                    .State));

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>A second-batch failure restores the exact pre-import owner graph and detached inputs.</summary>
    [Theory]
    [InlineData("subtree", 65)]
    [InlineData("subtree", 130)]
    [InlineData("forest", 130)]
    public async Task InsertPreparedOwnerImportAsync_PreparedOwnerImportLateFailure_RestoresExactState(
        string shape,
        int nodes
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedPreparedNavigationDestinationAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var failure = new PreparedOwnerBatchFailure();
        await using var context = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken,
            failure);

        var graph = await PrepareOwnerImportAsync(
            context,
            nodes,
            cancellationToken: TestContext.Current.CancellationToken);

        var before = CapturePlannedParentState(context);
        var inputBefore = CapturePreparedOwnerInputs(graph.Inputs);
        var storedBefore = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var error = await Record.ExceptionAsync(() =>
            InsertPreparedOwnerImportAsync(context, shape, graph.Inputs, CancellationToken.None));

        var after = CapturePlannedParentState(context);
        var inputAfter = CapturePreparedOwnerInputs(graph.Inputs);
        var storedAfter = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(failure.Error, error);
        Assert.Equal(2, failure.CompletedSaves);
        AssertPlannedParentState(before, after);
        Assert.Equal(inputBefore, inputAfter);
        Assert.All(graph.Inputs, input => Assert.Empty(input.Children));
        Assert.Equal(storedBefore.Nodes, storedAfter.Nodes);
        Assert.Equal(storedBefore.Note, storedAfter.Note);
        Assert.Equal(0, storedAfter.Issues);
        Assert.All(
            graph.Inputs,
            input => Assert.Equal(
                EntityState.Detached,
                context.Entry(input)
                    .State));

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>The same import retries without repairing its owner collection or structural input state.</summary>
    [Theory]
    [InlineData("subtree", 65)]
    [InlineData("subtree", 130)]
    [InlineData("forest", 130)]
    public async Task InsertPreparedOwnerImportAsync_PreparedOwnerImport_RetriesIdenticallyAfterLateFailure(
        string shape,
        int nodes
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedPreparedNavigationDestinationAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var failure = new PreparedOwnerBatchFailure();
        await using var context = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken,
            failure);

        var graph = await PrepareOwnerImportAsync(
            context,
            nodes,
            cancellationToken: TestContext.Current.CancellationToken);

        var before = CapturePlannedParentState(context);
        var inputBefore = CapturePreparedOwnerInputs(graph.Inputs);

        // WHY: The first rollback is the retry's precondition; this observer fails only its second save.
        var firstError = await Record.ExceptionAsync(() =>
            InsertPreparedOwnerImportAsync(context, shape, graph.Inputs, CancellationToken.None));

        var afterFailure = CapturePlannedParentState(context);
        var inputAfterFailure = CapturePreparedOwnerInputs(graph.Inputs);

        // Act
        await InsertPreparedOwnerImportAsync(context, shape, graph.Inputs, CancellationToken.None);
        var stored = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(failure.Error, firstError);
        AssertPlannedParentState(before, afterFailure);
        Assert.Equal(inputBefore, inputAfterFailure);
        Assert.Equal(4 + nodes, stored.Nodes.Length);
        Assert.Equal("original", stored.Note);
        Assert.Equal(0, stored.Issues);
        Assert.Equal(graph.Inputs, graph.Owner.Nodes, ReferenceEqualityComparer.Instance);
        Assert.All(
            graph.Inputs,
            input => Assert.Equal(
                EntityState.Detached,
                context.Entry(input)
                    .State));

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Cancellation between batches also restores inputs that have not entered their active batch.</summary>
    [Theory]
    [InlineData("subtree", 65)]
    [InlineData("subtree", 130)]
    [InlineData("forest", 130)]
    public async Task InsertPreparedOwnerImportAsync_PreparedOwnerImportCancellation_RestoresExactState(
        string shape,
        int nodes
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedPreparedNavigationDestinationAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        using var cancellation = new CancellationTokenSource();
        var observer = new PreparedOwnerBatchCancellation(cancellation);
        await using var context = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken,
            observer);

        var graph = await PrepareOwnerImportAsync(
            context,
            nodes,
            cancellationToken: TestContext.Current.CancellationToken);

        var before = CapturePlannedParentState(context);
        var inputBefore = CapturePreparedOwnerInputs(graph.Inputs);
        var storedBefore = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var error = await Record.ExceptionAsync(() =>
            InsertPreparedOwnerImportAsync(context, shape, graph.Inputs, cancellation.Token));

        var after = CapturePlannedParentState(context);
        var inputAfter = CapturePreparedOwnerInputs(graph.Inputs);
        var storedAfter = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.Equal(1, observer.CompletedSaves);
        AssertPlannedParentState(before, after);
        Assert.Equal(inputBefore, inputAfter);
        Assert.All(graph.Inputs, input => Assert.Empty(input.Children));
        Assert.Equal(storedBefore.Nodes, storedAfter.Nodes);
        Assert.Equal(storedBefore.Note, storedAfter.Note);
        Assert.Equal(0, storedAfter.Issues);
        Assert.All(
            graph.Inputs,
            input => Assert.Equal(
                EntityState.Detached,
                context.Entry(input)
                    .State));

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }
}

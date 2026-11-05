using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Cleanup visits every attached future input even when the first detachment callback throws.</summary>
    [Theory]
    [InlineData("subtree")]
    [InlineData("forest")]
    public async Task PreparedOwnerImportContinuesFutureCleanupAfterCallbackFailure(
        string shape
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedPreparedNavigationDestinationAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var callback = new SaveBoundaryCallback();
        var probe = new NavigationWriteProbe();
        await using var context = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken,
            callback,
            probe);

        var graph = await PrepareOwnerImportAsync(
            context,
            130,
            cancellationToken: TestContext.Current.CancellationToken);

        var firstFuture = graph.Inputs[64];
        var secondFuture = graph.Inputs[129];
        var inputBefore = CapturePreparedOwnerInputs(graph.Inputs);
        var storedBefore = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var cleanupError = new InvalidOperationException("Injected future-input detachment callback failure.");
        var futureAttempts = new List<NavigationNode>();
        var activeAttempts = new List<int>();
        var cleanupThrew = false;
        context.ChangeTracker.StateChanged += (_, change) =>
        {
            if (change is not { OldState: EntityState.Added, NewState: EntityState.Detached }
                || change.Entry.Entity is not NavigationNode node)
            {
                return;
            }

            if (ReferenceEquals(node, firstFuture)
                || ReferenceEquals(node, secondFuture))
            {
                futureAttempts.Add(node);

                if (!cleanupThrew)
                {
                    // WHY: Fail only the first future cleanup attempt; the second must still be visited afterward.
                    cleanupThrew = true;

                    throw cleanupError;
                }
            }
            else
            {
                activeAttempts.Add(node.Id);
            }
        };

        int? writesAtCallback = null;
        int? activeAtCallback = null;
        int? registeredAtCallback = null;
        callback.OnSaving = current =>
        {
            var updates = current
                .GetService<IUpdateAdapterFactory>()
                .Create();

            writesAtCallback = probe.Writes;
            activeAtCallback = updates.Entries.Count(entry => entry.EntityType.ClrType == typeof(NavigationNode));

            // WHY: Both inputs belong to the validated operation, but neither belongs to the active write batch.
            current.Entry(firstFuture)
                .State = EntityState.Added;

            current.Entry(secondFuture)
                .State = EntityState.Added;

            registeredAtCallback = updates.Entries.Count(entry => entry.EntityType.ClrType == typeof(NavigationNode));
        };

        // Act
        var error = await Record.ExceptionAsync(() =>
            InsertPreparedOwnerImportAsync(context, shape, graph.Inputs, CancellationToken.None));

        var inputAfter = CapturePreparedOwnerInputs(graph.Inputs);
        var storedAfter = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var aggregate = Assert.IsType<AggregateException>(error);
        var failures = aggregate.Flatten()
            .InnerExceptions;

        var guard = Assert.Single(failures.OfType<NestedSetException>());
        Assert.Equal(NestedSetErrorCode.InvalidImport, guard.Code);
        Assert.Equal("Save callbacks must not add or remove hierarchy entities during an import.", guard.Message);
        Assert.Equal(2, failures.Count);
        Assert.Contains(failures, failure => ReferenceEquals(failure, cleanupError));
        Assert.Contains("Discard the context.", aggregate.Message, StringComparison.Ordinal);
        Assert.Equal(64, activeAtCallback);
        Assert.Equal(66, registeredAtCallback);
        Assert.Equal(writesAtCallback, probe.Writes);
        Assert.True(cleanupThrew);
        Assert.Equal(2, futureAttempts.Count);
        Assert.NotSame(futureAttempts[0], futureAttempts[1]);
        Assert.Contains(firstFuture, futureAttempts);
        Assert.Contains(secondFuture, futureAttempts);
        Assert.Equal(Enumerable.Range(1000, 64), activeAttempts.Order());
        Assert.Equal(inputBefore.Take(64), inputAfter.Take(64));
        Assert.All(graph.Inputs.Take(64), input => Assert.Empty(input.Children));
        Assert.All(
            graph.Inputs,
            input => Assert.Equal(
                EntityState.Detached,
                context.Entry(input)
                    .State));

        Assert.Equal(storedBefore.Nodes, storedAfter.Nodes);
        Assert.Equal(storedBefore.Note, storedAfter.Note);
        Assert.Equal(0, storedAfter.Issues);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }
}

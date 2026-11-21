using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
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
    public async Task InsertPreparedOwnerImportAsync_PreparedOwnerImport_RejectsUnauthorizedFutureMembers(
        string shape,
        int nodes,
        string change
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
            nodes,
            cancellationToken: TestContext.Current.CancellationToken);

        var future = graph.Inputs[^1];

        if (change == "scope")
        {
            // WHY: Raw input scope is staging-owned; membership must use its bound plan scope instead.
            future.Scope = graph.OtherOwner.Id;
        }

        var before = CapturePlannedParentState(context);
        var inputBefore = CapturePreparedOwnerInputs(graph.Inputs);
        var storedBefore = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        int? writesAtCallback = null;
        int? activeAtCallback = null;
        callback.OnSaving = current =>
        {
            writesAtCallback = probe.Writes;
            activeAtCallback = current
                .GetService<IUpdateAdapterFactory>()
                .Create()
                .Entries
                .Count(entry => entry.EntityType.ClrType == typeof(NavigationNode));

            switch (change)
            {
                case "unknown":
                    graph.Owner.Nodes.Add(
                        new NavigationNode
                        {
                            Id = 999,
                            Scope = graph.Owner.Id,
                            Name = "Unknown",
                        });

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
        var storedAfter = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var rejection = Assert.IsType<NestedSetException>(error);

        if (change == "added")
        {
            // WHY: An explicitly attached future node violates the existing active import-membership contract.
            Assert.Equal(NestedSetErrorCode.InvalidImport, rejection.Code);
            Assert.Equal(
                "Save callbacks must not add or remove hierarchy entities during an import.",
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
        Assert.All(
            graph.Inputs,
            input => Assert.Equal(
                EntityState.Detached,
                context.Entry(input)
                    .State));

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }
}

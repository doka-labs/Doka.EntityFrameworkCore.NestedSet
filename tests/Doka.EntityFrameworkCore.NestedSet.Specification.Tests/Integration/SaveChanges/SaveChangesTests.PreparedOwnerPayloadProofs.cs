using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Prepared future composite relationships read live ordinary FK values at every guarded wave.</summary>
    [Theory]
    [InlineData("subtree", 65, false)]
    [InlineData("subtree", 65, true)]
    [InlineData("forest", 130, false)]
    [InlineData("forest", 130, true)]
    public async Task PreparedOwnerImportFuturePayloadProofRequiresLiveForeignKey(
        string shape,
        int nodes,
        bool changeFuturePayload
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedPreparedNavigationDestinationAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);
        await SeedPayloadOwnersAsync(database, assigned: false,
            cancellationToken: TestContext.Current.CancellationToken);
        var callback = new SaveBoundaryCallback();
        var probe = new NavigationWriteProbe();
        await using var context = await CreateNavigationContextAsync(database, wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken,
            callback, probe);

        var graph = await PrepareOwnerImportAsync(context, nodes,
            cancellationToken: TestContext.Current.CancellationToken);

        var payloadOwner = await context.Set<NavigationPayloadOwner>().Include(owner => owner.Nodes)
            .SingleAsync(owner => owner.Scope == graph.Owner.Id && owner.Id == 1, CancellationToken.None);

        foreach (var input in graph.Inputs)
        {
            input.PayloadOwnerId = payloadOwner.Id;
        }

        payloadOwner.Nodes.AddRange(graph.Inputs);
        var collection = payloadOwner.Nodes;
        var members = collection.ToArray();
        var future = graph.Inputs[^1];
        var before = CapturePlannedParentState(context);
        var inputBefore = CapturePreparedOwnerInputs(graph.Inputs);
        var expectedInputs = inputBefore.ToArray();

        if (changeFuturePayload)
        {
            // WHY: Ordinary caller payload is preserved on rollback; only library-staged structure is restored.
            expectedInputs[^1] = expectedInputs[^1] with { PayloadOwnerId = 2 };
        }

        var storedBefore = await ReadPreparedOwnerStoreAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        var callbacks = 0;
        int? activeAtCallback = null;
        int? writesAtCallback = null;
        bool? membersUnchanged = null;
        callback.OnSaving = current =>
        {
            callbacks++;

            if (callbacks != 1)
            {
                return;
            }

            activeAtCallback = current.GetService<IUpdateAdapterFactory>().Create().Entries
                .Count(entry => entry.EntityType.ClrType == typeof(NavigationNode));

            writesAtCallback = probe.Writes;

            if (changeFuturePayload)
            {
                // WHY: Identical owner references/count do not prove that a detached future FK still matches it.
                future.PayloadOwnerId = 2;
            }

            membersUnchanged = members.SequenceEqual(payloadOwner.Nodes, ReferenceEqualityComparer.Instance);
        };

        // Act
        var error = await Record.ExceptionAsync(() =>
            InsertPreparedOwnerImportAsync(context, shape, graph.Inputs, CancellationToken.None));

        var after = CapturePlannedParentState(context);
        var inputAfter = CapturePreparedOwnerInputs(graph.Inputs);

        if (!changeFuturePayload && error is null)
        {
            callback.OnSaving = null;
            graph.NoteValue.CurrentValue = "following payload";
            await context.SaveChangesAsync(CancellationToken.None);
        }

        var storedAfter = await ReadPreparedOwnerStoreAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        await using var verification = await CreateNavigationContextAsync(database, wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken);

        var storedOwners = await verification.Set<NavigationNode>().Where(node => node.Id >= 1000)
            .Select(node => node.PayloadOwnerId).ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(64, activeAtCallback);
        Assert.True(membersUnchanged);
        Assert.Same(collection, payloadOwner.Nodes);
        Assert.Equal(members, payloadOwner.Nodes, ReferenceEqualityComparer.Instance);

        if (changeFuturePayload)
        {
            Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(error).Code);
            Assert.Equal(writesAtCallback, probe.Writes);
            AssertPlannedParentState(before, after);
            Assert.Equal(expectedInputs, inputAfter);
            Assert.All(graph.Inputs, input => Assert.Empty(input.Children));
            Assert.Equal(2, future.PayloadOwnerId);
            Assert.Equal(storedBefore.Nodes, storedAfter.Nodes);
            Assert.Equal(storedBefore.Note, storedAfter.Note);
            Assert.Empty(storedOwners);
        }
        else
        {
            Assert.Null(error);
            Assert.Equal(4 + nodes, storedAfter.Nodes.Length);
            Assert.Equal(nodes, storedOwners.Length);
            Assert.All(storedOwners, owner => Assert.Equal(1, owner));
            Assert.Equal("following payload", storedAfter.Note);
        }

        Assert.Equal(0, storedAfter.Issues);
        Assert.All(graph.Inputs, input => Assert.Equal(EntityState.Detached, context.Entry(input).State));
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }
}

using TopologyContext = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyContext;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks complete complex-graph capture and exceptional restoration against independent native/scalar preimages.</summary>
public sealed partial class ComplexTopologyGraphRecoveryTests
{
    /// <summary>A single wide complex graph restores its exact original topology and native flags.</summary>
    /// <param name="rows">The complex-element count within one tracked entity, with two leaves per element.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(128)]
    [InlineData(2_048)]
    [InlineData(20_000)]
    public async Task Capture_FullCaptureAndExceptionalRestore_RestoresOriginalGraph(
        int rows
    )
    {
        // Arrange
        await using var context = new TopologyContext();
        var payload = CreatePayload(rows);
        var entry = context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        MarkPending(entry);
        var originalRows = payload.Rows!;
        var items = originalRows.ToArray();
        var details = items
            .Select(item => item.Details)
            .ToArray();

        var leafLists = items
            .Select(item => item.Leaves!)
            .ToArray();

        var leaves = leafLists
            .Select(list => list.ToArray())
            .ToArray();

        var optional = payload.Optional;
        var expectedScalars = ObserveScalars(entry);
        var expectedStates = ObserveStates(entry);
        var originalState = entry.State;
        var snapshotEntry = NestedSetInsertionContract.Instance.Entry(entry);

        // Act
        var snapshot = NestedSetTrackerSnapshot.Capture(context);

        var failure = Record.Exception(
            (Action)(() =>
            {
                context.ChangeTracker.AcceptAllChanges();
                payload.Optional!.Text = "Callback optional";
                payload.Optional = null;

                foreach (var item in items)
                {
                    item.Text = "Callback item";
                    item.Details = null;
                    item.Leaves![0].Text = "Callback leaf";
                    item.Leaves.Clear();
                }

                originalRows.Clear();
                payload.Rows = [];

                throw new TopologyCallbackFailure();
            }));

        snapshot.Restore();

        // Assert
        Assert.IsType<TopologyCallbackFailure>(failure);
        Assert.Same(snapshotEntry, NestedSetInsertionContract.Instance.Entry(entry));
        Assert.Equal(originalState, entry.State);
        Assert.Same(originalRows, payload.Rows);
        Assert.Same(optional, payload.Optional);
        Assert.Equal(rows, originalRows.Count);

        // WHY: Preserve exact original membership by ordinal; scalar equality alone accepts replacement clones.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < rows; index++)
        {
            Assert.Same(items[index], originalRows[index]);
            Assert.Same(details[index], items[index].Details);
            Assert.Same(leafLists[index], items[index].Leaves);
            Assert.Equal(2, leafLists[index].Count);
            Assert.Same(leaves[index][0], leafLists[index][0]);
            Assert.Same(leaves[index][1], leafLists[index][1]);
        }

        Assert.Equal(expectedScalars, ObserveScalars(entry));
        Assert.Equal(expectedStates, ObserveStates(entry));
        Assert.Single(context.ChangeTracker.Entries());
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }
}

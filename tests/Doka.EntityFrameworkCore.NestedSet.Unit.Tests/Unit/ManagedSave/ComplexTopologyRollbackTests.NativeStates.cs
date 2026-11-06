namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexTopologyRollbackTests
{
    /// <summary>Detected pending deletion or reordering survives callback changes and later acceptance.</summary>
    /// <param name="pendingChange">The caller's detected topology change already present at snapshot capture.</param>
    /// <param name="accepted">Whether the callback graph was accepted before a later transaction failure.</param>
    [Theory]
    [InlineData("remove", false)]
    [InlineData("remove", true)]
    [InlineData("reverse", false)]
    [InlineData("reverse", true)]
    public async Task PendingNativeTopologySurvivesCallbackAndAcceptance(
        string pendingChange,
        bool accepted
    )
    {
        // Arrange
        await using var context = new TopologyContext();
        var entry = TrackGraph(context);

        if (pendingChange == "remove")
        {
            entry.Entity.Rows!.RemoveAt(0);
        }
        else
        {
            entry.Entity.Rows!.Reverse();
        }

        context.ChangeTracker.DetectChanges();
        var before = Observe(entry);
        var collection = entry.ComplexCollection(nameof(TopologyPayload.Rows));
        var originalEntries = Enumerable
            .Range(0, 2)
            .Select(index =>
            {
                var original = collection.GetOriginalEntry(index);

                return new OriginalElementObservation(
                    original.State,
                    ObserveScalar(original.Property(nameof(TopologyItem.Text))));
            })
            .ToArray();

        var write = Update(context, entry.Entity);

        // WHY: The managed-save overload captures the actual provider write set. Later acceptance can retire
        // deleted original ordinals, so restoring only the callback's surviving current entries is insufficient.
        var snapshot = NestedSetTrackerSnapshot.Capture(context, [write]);
        entry.Entity.Rows!.Clear();
        entry.Entity.Optional = null;
        context.ChangeTracker.DetectChanges();

        if (accepted)
        {
            context.ChangeTracker.AcceptAllChanges();
        }

        // Act
        snapshot.Restore();
        snapshot.Restore();

        // Assert
        Assert.Equal(EntityState.Modified, before.State);
        Assert.True(before.RowsModified);
        Assert.Equal("Stored first", originalEntries[0].Text.Original);
        Assert.Equal("Stored last", originalEntries[1].Text.Original);

        if (pendingChange == "remove")
        {
            Assert.Single(before.Items);
            Assert.Equal(EntityState.Deleted, originalEntries[0].State);
        }
        else
        {
            Assert.Equal(2, before.Items.Length);
            Assert.Equal("Stored last", before.Items[0].Text.Current);
        }

        AssertGraph(entry, before);

        // WHY: Deleted originals are absent from the current list, but their native slots still define the
        // caller's pending delete and must remain independently available after a failed accepted save.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < originalEntries.Length; index++)
        {
            var expected = originalEntries[index];
            var original = collection.GetOriginalEntry(index);

            Assert.Equal(expected.State, original.State);
            AssertScalar(original.Property(nameof(TopologyItem.Text)), expected.Text);
        }

        AssertSettings(context);
    }

    /// <summary>Retains original native element state even when the element is absent from the current list.</summary>
    private sealed record OriginalElementObservation(
        EntityState State,
        ScalarObservation Text
    );
}

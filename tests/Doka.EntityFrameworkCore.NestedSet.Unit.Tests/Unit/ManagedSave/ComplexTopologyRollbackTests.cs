namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Restores pre-callback complex topology without losing the caller's prior graph or native state.</summary>
public sealed partial class ComplexTopologyRollbackTests
{
    /// <summary>Callback topology changes restore both raw and explicitly detected native graph state.</summary>
    /// <param name="change">The callback's mutation after the tracker snapshot has been captured.</param>
    /// <param name="detectCallback">Whether EF detects the callback change before the save fails.</param>
    [Theory]
    [InlineData("removeFirst", false)]
    [InlineData("removeFirst", true)]
    [InlineData("removeLast", false)]
    [InlineData("removeLast", true)]
    [InlineData("clear", false)]
    [InlineData("clear", true)]
    [InlineData("nestedClear", false)]
    [InlineData("nestedClear", true)]
    [InlineData("nestedNull", false)]
    [InlineData("nestedNull", true)]
    [InlineData("collectionNull", false)]
    [InlineData("collectionNull", true)]
    [InlineData("optionalNull", false)]
    [InlineData("optionalNull", true)]
    [InlineData("optionalReplace", false)]
    [InlineData("optionalReplace", true)]
    [InlineData("elementOptionalNull", false)]
    [InlineData("elementOptionalNull", true)]
    [InlineData("listReplace", false)]
    [InlineData("listReplace", true)]
    [InlineData("reverse", false)]
    [InlineData("reverse", true)]
    [InlineData("add", false)]
    [InlineData("add", true)]
    public async Task Restore_CallbackTopology_RestoresCapturedGraph(
        string change,
        bool detectCallback
    )
    {
        // Arrange
        await using var context = new TopologyContext();
        var entry = TrackGraph(context);
        var unrelated = TrackUnrelated(context);
        var before = Observe(entry);
        var unrelatedBefore = ObserveScalar(unrelated.Property(value => value.Name));
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        Mutate(entry.Entity, change);
        unrelated.Entity.Name = "Callback unrelated";
        unrelated.Property(value => value.Name)
            .IsModified = true;

        if (detectCallback)
        {
            context.ChangeTracker.DetectChanges();
        }

        // Act
        snapshot.Restore();

        // Assert
        Assert.Equal(EntityState.Unchanged, before.State);
        Assert.Equal("Pending marker", before.Marker.Current);
        Assert.Equal("Stored marker", before.Marker.Original);
        AssertGraph(entry, before);
        AssertScalar(unrelated.Property(value => value.Name), unrelatedBefore);
        Assert.Equal("Pending unrelated", unrelated.Entity.Name);
        Assert.Equal(EntityState.Unchanged, unrelated.State);
        AssertSettings(context);
    }

    /// <summary>Sharing promotion and previously Modified payload retain distinct original state and intent.</summary>
    /// <param name="mode">A flagless sharing peer or an already Modified ordinary payload.</param>
    /// <param name="detectCallback">Whether the callback removal is explicitly detected.</param>
    [Theory]
    [InlineData("shared", false)]
    [InlineData("shared", true)]
    [InlineData("modified", false)]
    [InlineData("modified", true)]
    public async Task Restore_RemovedElement_RestoresSharedOrModifiedState(
        string mode,
        bool detectCallback
    )
    {
        // Arrange
        await using var context = mode == "shared"
            ? new SharedTopologyContext()
            : new TopologyContext();

        var entry = TrackGraph(context);

        if (mode == "modified")
        {
            entry.Property(value => value.Marker)
                .IsModified = true;
        }

        var before = Observe(entry);
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        entry.Entity.Rows!.RemoveAt(0);

        if (mode == "shared")
        {
            // WHY: IUpdateEntry uses EF's real flagless provider-promotion path rather than marking payload.
            Update(context, entry.Entity)
                .EntityState = EntityState.Modified;
        }

        if (detectCallback)
        {
            context.ChangeTracker.DetectChanges();
        }

        // Act
        snapshot.Restore();

        // Assert
        Assert.Equal(
            mode == "modified"
                ? EntityState.Modified
                : EntityState.Unchanged,
            before.State);

        AssertGraph(entry, before);
        Assert.Equal(
            mode == "modified",
            entry.Property(value => value.Marker)
                .IsModified);

        AssertSettings(context);
    }

    /// <summary>A pending addition survives a callback that clears the captured list.</summary>
    /// <param name="detectPending">Whether the caller explicitly detected its pending addition before capture.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_PendingAddition_SurvivesDetectedCallbackClear(
        bool detectPending
    )
    {
        // Arrange
        await using var context = new TopologyContext();
        var entry = TrackGraph(context);
        entry.Entity.Rows!.Add(
            new TopologyItem
            {
                Text = "Pending item",
                Details = new TopologyDetails
                {
                    Text = "Pending details",
                    Code = "Pending code",
                },
                Leaves = [new TopologyLeaf { Text = "Pending leaf" }],
            });

        if (detectPending)
        {
            context.ChangeTracker.DetectChanges();
        }

        var before = Observe(entry);
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        entry.Entity.Rows!.Clear();
        context.ChangeTracker.DetectChanges();

        // Act
        snapshot.Restore();

        // Assert
        Assert.Equal(3, before.Items.Length);
        Assert.Equal("Pending item", before.Items[2].Text.Current);
        Assert.Equal(
            detectPending
                ? EntityState.Added
                : EntityState.Detached,
            before.Items[2].State);

        AssertGraph(entry, before);
        AssertSettings(context);
    }

    /// <summary>Raw-null containers captured over stored originals remain null after callback initialization.</summary>
    /// <param name="detectCallback">Whether EF observes the newly initialized callback graph.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_CapturedNullContainers_SurviveCallbackInitialization(
        bool detectCallback
    )
    {
        // Arrange
        await using var context = new TopologyContext();
        var entry = TrackGraph(context);
        entry.Entity.Rows = null;
        entry.Entity.Optional = null;
        var before = Observe(entry);
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        entry.Entity.Rows =
        [
            new TopologyItem
            {
                Text = "Callback item",
                Leaves = [],
            },
        ];

        entry.Entity.Optional = new TopologyDetails
        {
            Text = "Callback details",
            Code = "Callback code",
        };

        if (detectCallback)
        {
            context.ChangeTracker.DetectChanges();
        }

        // Act
        snapshot.Restore();

        // Assert
        Assert.Null(before.Rows);
        Assert.Null(before.Optional.Instance);
        Assert.Equal("Stored details", before.Optional.Text.Original);
        AssertGraph(entry, before);
        AssertSettings(context);
    }

    /// <summary>Repeated recovery preserves native ordinals after removal and nested container replacement.</summary>
    [Fact]
    public async Task Restore_RepeatedRestore_PreservesRestoredNativeOrdinals()
    {
        // Arrange
        await using var context = new TopologyContext();
        var entry = TrackGraph(context);
        var before = Observe(entry);
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        entry.Entity.Rows![0].Leaves = [new TopologyLeaf { Text = "Callback leaf" }];
        entry.Entity.Rows!.RemoveAt(0);
        context.ChangeTracker.DetectChanges();

        // Act
        snapshot.Restore();
        snapshot.Restore();

        // Assert
        AssertGraph(entry, before);
        AssertSettings(context);
    }
}

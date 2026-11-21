namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexTopologyRollbackTests
{
    /// <summary>Restores an optional JSON document and native children after callback removal or replacement.</summary>
    /// <param name="replace">Whether the callback replaces the document instead of setting it null.</param>
    /// <param name="detectCallback">Whether EF detects the callback's new topology.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Restore_OptionalDocument_RestoresCollectionContainers(
        bool replace,
        bool detectCallback
    )
    {
        // Arrange
        await using var context = new OptionalCollectionContext();
        var payload = CreateOptionalPayload();
        var entry = context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        var document = payload.Document!;
        payload.Marker = "Pending optional marker";
        document.Title = "Pending document";
        document.Children![0].Text = "Pending document child";
        var before = ObserveDocument(entry, originalCount: 2);
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        payload.Document = replace
            ? CreateCallbackDocument()
            : null;

        if (detectCallback)
        {
            context.ChangeTracker.DetectChanges();
        }

        // WHY: EF may reuse existing native ordinal entries when their optional container changes. Those
        // entries belong to the captured preimage; only genuinely Added callback handles should be retired.
        var introduced = replace && detectCallback
            ? ReadDocumentEntries(entry, count: 2)
                .Where(element => element.State == EntityState.Added)
                .ToArray()
            : [];

        // Act
        snapshot.Restore();
        snapshot.Restore();

        // Assert
        Assert.Equal(EntityState.Unchanged, before.State);
        Assert.Equal("Pending document", before.Title.Current);
        Assert.Equal("Stored document", before.Title.Original);
        AssertDocument(entry, before);
        Assert.All(introduced, element => Assert.Equal(EntityState.Detached, element.State));
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Introduced nested entries are retired when the captured optional document returns to null.</summary>
    /// <param name="literalNull">Whether null was originally tracked or set raw over a stored document.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_NullDocument_RetiresIntroducedNativeChildren(
        bool literalNull
    )
    {
        // Arrange
        await using var context = new OptionalCollectionContext();
        var payload = CreateOptionalPayload();

        if (literalNull)
        {
            payload.Document = null;
        }

        var entry = context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        payload.Document = null;
        var before = ObserveDocument(
            entry,
            originalCount: literalNull
                ? 0
                : 2);

        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        payload.Document = CreateCallbackDocument();
        context.ChangeTracker.DetectChanges();
        var callbackEntries = ReadDocumentEntries(entry, count: 2);
        var callbackStates = callbackEntries
            .Select(element => element.State)
            .ToArray();

        var introduced = callbackEntries
            .Where(element => element.State == EntityState.Added)
            .ToArray();

        // Act
        snapshot.Restore();
        snapshot.Restore();

        // Assert
        Assert.Null(before.Instance);
        Assert.Null(before.Children);
        Assert.Equal(
            literalNull
                ? null
                : "Stored document",
            before.Title.Original);

        if (literalNull)
        {
            // WHY: A literal-null original has no native entries to reuse. Require both actual additions so
            // retirement cannot pass vacuously after the optional reference is hidden again.
            Assert.Equal(2, introduced.Length);
            Assert.All(callbackStates, state => Assert.Equal(EntityState.Added, state));
        }

        AssertDocument(entry, before);
        Assert.All(introduced, element => Assert.Equal(EntityState.Detached, element.State));
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>A hidden modified child retains its original buffer, flag and raw CLR value after rollback.</summary>
    /// <param name="accepted">Whether EF accepted the callback's replacement document before rollback.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_HiddenModifiedDocumentChildren_RetainFlagsAfterAcceptance(
        bool accepted
    )
    {
        // Arrange
        await using var context = new OptionalCollectionContext();
        var payload = CreateOptionalPayload();
        var entry = context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        var originalDocument = payload.Document!;
        var originalChildren = originalDocument.Children!;
        var originalChild = originalChildren[0];
        var collection = entry
            .ComplexProperty(value => value.Document)
            .ComplexCollection(nameof(OptionalDocument.Children));

        // WHY: Keep the actual native handles before hiding their CLR parent. Under null, the current getter
        // cannot read that path, but the original buffer and modified flag still belong to the pending write.
        var originalEntry = collection.GetOriginalEntry(0);
        var originalProperty = originalEntry.Property(nameof(OptionalChild.Text));
        originalChild.Text = "Pending hidden child";
        originalProperty.IsModified = true;
        var originalValue = originalProperty.OriginalValue;
        payload.Document = null;
        var before = ObserveDocument(entry, originalCount: 2);
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        payload.Document = CreateCallbackDocument();
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
        Assert.Equal(EntityState.Modified, before.Originals[0].State);
        Assert.True(before.Originals[0].Modified);
        Assert.Equal("Stored child first", originalValue);
        AssertDocument(entry, before);
        Assert.Null(payload.Document);
        Assert.Equal(EntityState.Modified, originalEntry.State);
        Assert.True(originalProperty.IsModified);
        Assert.Equal(originalValue, originalProperty.OriginalValue);
        Assert.Same(originalChildren, originalDocument.Children);
        Assert.Same(originalChild, originalDocument.Children![0]);
        Assert.Equal("Pending hidden child", originalChild.Text);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>A captured raw removal retains its native tail through callback acceptance.</summary>
    /// <param name="accepted">Whether EF accepted the later callback's empty collection before rollback.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_RawOuterRemoval_PreservesNativeTailAfterCallback(
        bool accepted
    )
    {
        // Arrange
        await using var context = new TopologyContext();
        var entry = TrackGraph(context);
        entry.Entity.Rows!.RemoveAt(1);
        var before = Observe(entry);
        var collection = entry.ComplexCollection(nameof(TopologyPayload.Rows));
        var originals = Enumerable
            .Range(0, 2)
            .Select(index => ObserveOriginal(collection.GetOriginalEntry(index)))
            .ToArray();

        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        entry.Entity.Rows!.Clear();
        context.ChangeTracker.DetectChanges();

        if (accepted)
        {
            context.ChangeTracker.AcceptAllChanges();
        }

        // Act
        snapshot.Restore();
        snapshot.Restore();

        // Assert
        Assert.Equal(EntityState.Unchanged, before.State);
        Assert.Single(before.Items);
        Assert.Equal("Stored last", originals[1].Value);
        AssertGraph(entry, before);

        // WHY: A raw pre-save removal leaves a real native tail beyond the current CLR count. It must not be
        // detected or discarded, and reading its current CLR value would itself be an invalid ordinal access.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < originals.Length; index++)
        {
            AssertOriginal(collection.GetOriginalEntry(index), originals[index]);
        }

        AssertSettings(context);
    }
}

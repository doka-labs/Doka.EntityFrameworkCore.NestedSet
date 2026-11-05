using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Preserves table-sharing peer state independently of current values and exact property flags.</summary>
public sealed partial class TableSharingRollbackTests
{
    /// <summary>A flagless provider promotion restores the captured state without accepting raw payload.</summary>
    /// <param name="owned">Whether the valid sharing peer uses EF ownership or ordinary table splitting.</param>
    /// <param name="capturedState">The actual detail state before the command preparer visits the shared row.</param>
    /// <param name="modifiedLabel">Whether only the detail's Label property is intentionally marked.</param>
    [Theory]
    [InlineData(true, EntityState.Unchanged, false)]
    [InlineData(false, EntityState.Unchanged, false)]
    [InlineData(true, EntityState.Modified, false)]
    [InlineData(false, EntityState.Modified, false)]
    [InlineData(true, EntityState.Modified, true)]
    [InlineData(false, EntityState.Modified, true)]
    public async Task SharingPeerPromotionRestoresExactStateAndUnmarkedRawPayload(
        bool owned,
        EntityState capturedState,
        bool modifiedLabel
    )
    {
        // Arrange
        await using var context = owned
            ? new SharingContext()
            : new SplitSharingContext();

        var detail = new SharingDetail { Id = 7 };
        var root = new SharingRoot
        {
            Id = 7,
            Detail = detail,
        };

        detail.Root = root;
        context.Attach(root);
        var rootEntry = context.Entry(root);
        var detailEntry = context.Entry(detail);
        SeedVersion(rootEntry);
        SeedVersion(detailEntry);
        root.Name = "Pending root";
        rootEntry.Property(value => value.Name)
            .IsModified = true;

        detail.Label = "Pending label";
        detail.Count = 8;

        if (modifiedLabel)
        {
            detailEntry.Property(value => value.Label)
                .IsModified = true;
        }

        var update = Update(context, detail);

        if (capturedState == EntityState.Modified
            && !modifiedLabel)
        {
            update.EntityState = EntityState.Modified;
        }

        var relationalModel = context.Model.GetRelationalModel();
        var table = rootEntry
            .Metadata
            .GetTableMappings()
            .Single()
            .Table;

        var detailTable = detailEntry
            .Metadata
            .GetTableMappings()
            .Single()
            .Table;

        var beforeState = update.EntityState;
        var beforeFlags = ModifiedProperties(update);
        var labelOriginal = detailEntry.Property(value => value.Label)
            .OriginalValue;

        var countOriginal = detailEntry.Property(value => value.Count)
            .OriginalValue;

        var snapshot = NestedSetTrackerSnapshot.Capture(context);

        // WHY: This exact public provider setter uses modifyProperties:false, as EF's command preparer does.
        // Scalar edits or readback here would clear flags during rollback and could hide the flagless-state bug.
        update.EntityState = EntityState.Modified;
        var promotedState = update.EntityState;
        var promotedFlags = ModifiedProperties(update);
        string[] expectedFlags = modifiedLabel
            ? [nameof(SharingDetail.Label)]
            : [];

        // Act
        snapshot.Restore();

        // Assert
        Assert.NotEmpty(relationalModel.Tables);
        Assert.Same(table, detailTable);
        Assert.Equal(owned, detailEntry.Metadata.IsOwned());
        Assert.Null(update.SharedIdentityEntry);
        Assert.Equal(capturedState, beforeState);
        Assert.Equal(EntityState.Modified, promotedState);
        Assert.Equal(beforeFlags, promotedFlags);
        Assert.Equal(capturedState, update.EntityState);
        Assert.Equal(beforeFlags, ModifiedProperties(update));
        Assert.Equal(expectedFlags, beforeFlags);
        Assert.Equal("Pending label", detail.Label);
        Assert.Equal(
            labelOriginal,
            detailEntry.Property(value => value.Label)
                .OriginalValue);

        Assert.Equal("Stored label", labelOriginal);
        Assert.Equal(8, detail.Count);
        Assert.Equal(
            countOriginal,
            detailEntry.Property(value => value.Count)
                .OriginalValue);

        Assert.Equal(1, countOriginal);
        Assert.False(
            detailEntry.Property(value => value.Count)
                .IsModified);

        Assert.Equal("Pending root", root.Name);
        Assert.Equal(
            "Stored root",
            rootEntry.Property(value => value.Name)
                .OriginalValue);

        Assert.Equal(EntityState.Modified, rootEntry.State);
        Assert.True(
            rootEntry.Property(value => value.Name)
                .IsModified);

        Assert.Same(detail, root.Detail);
        Assert.Same(root, detail.Root);
        AssertVersion(rootEntry);
        AssertVersion(detailEntry);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Generated shadow values on both aliases restore alongside the unchanged sharing-peer state.</summary>
    /// <param name="owned">Whether the valid sharing peer uses ownership or ordinary table splitting.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedShadowReadbackRestoresBothAliasesAndRawPayload(
        bool owned
    )
    {
        // Arrange
        await using var context = owned
            ? new SharingContext()
            : new SplitSharingContext();

        var detail = new SharingDetail { Id = 7 };
        var root = new SharingRoot
        {
            Id = 7,
            Detail = detail,
        };

        detail.Root = root;
        context.Attach(root);
        var rootEntry = context.Entry(root);
        var detailEntry = context.Entry(detail);
        SeedVersion(rootEntry);
        SeedVersion(detailEntry);
        root.Name = "Pending root";
        rootEntry.Property(value => value.Name)
            .IsModified = true;

        detail.Label = "Pending label";
        detail.Count = 8;
        var rootUpdate = Update(context, root);
        var detailUpdate = Update(context, detail);
        var rootVersion = rootEntry
            .Property(VersionProperty)
            .Metadata;

        var detailVersion = detailEntry
            .Property(VersionProperty)
            .Metadata;

        var beforeState = detailUpdate.EntityState;
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        detailUpdate.EntityState = EntityState.Modified;

        // WHY: Shared-column readback reaches every mapped alias through the public provider interface.
        // This is generated shadow-state evidence only; no native SQL Server rowversion execution is claimed.
        rootUpdate.SetStoreGeneratedValue(rootVersion, new byte[] { 9 }, setModified: false);
        detailUpdate.SetStoreGeneratedValue(detailVersion, new byte[] { 9 }, setModified: false);
        var rootReturned = rootUpdate.HasStoreGeneratedValue(rootVersion);
        var detailReturned = detailUpdate.HasStoreGeneratedValue(detailVersion);

        // Act
        snapshot.Restore();

        // Assert
        Assert.Equal(EntityState.Unchanged, beforeState);
        Assert.True(rootVersion.IsShadowProperty());
        Assert.True(detailVersion.IsShadowProperty());
        Assert.Same(
            rootVersion
                .GetTableColumnMappings()
                .Single()
                .Column,
            detailVersion
                .GetTableColumnMappings()
                .Single()
                .Column);

        Assert.True(rootReturned);
        Assert.True(detailReturned);
        Assert.Equal(EntityState.Unchanged, detailUpdate.EntityState);
        Assert.Empty(ModifiedProperties(detailUpdate));
        AssertVersion(rootEntry);
        AssertVersion(detailEntry);
        Assert.False(rootUpdate.HasStoreGeneratedValue(rootVersion));
        Assert.False(detailUpdate.HasStoreGeneratedValue(detailVersion));
        Assert.Equal("Pending label", detail.Label);
        Assert.Equal(
            "Stored label",
            detailEntry.Property(value => value.Label)
                .OriginalValue);

        Assert.Equal(8, detail.Count);
        Assert.Equal(
            1,
            detailEntry.Property(value => value.Count)
                .OriginalValue);

        Assert.Equal(EntityState.Modified, rootEntry.State);
        Assert.True(
            rootEntry.Property(value => value.Name)
                .IsModified);

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Accepted additions return sharing aliases to their temporary identities and CLR sentinels.</summary>
    /// <param name="owned">Whether the generated sharing key propagates through ownership or table splitting.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptedAddedSharingAggregateRestoresTemporaryIdentity(
        bool owned
    )
    {
        // Arrange
        await using var context = owned
            ? new SharingContext()
            : new SplitSharingContext();

        var detail = new SharingDetail
        {
            Label = "Pending label",
            Count = 8,
        };

        var root = new SharingRoot
        {
            Name = "Pending root",
            Detail = detail,
        };

        detail.Root = root;
        var rootEntry = await context.AddAsync(root, CancellationToken.None);
        var detailEntry = context.Entry(detail);
        SeedVersion(rootEntry);
        SeedVersion(detailEntry);
        var rootUpdate = Update(context, root);
        var detailUpdate = Update(context, detail);
        var rootKey = rootEntry.Property(value => value.Id);
        var detailKey = detailEntry.Property(value => value.Id);
        var rootBefore = rootKey.CurrentValue;
        var detailBefore = detailKey.CurrentValue;
        var rootOriginalBefore = rootKey.OriginalValue;
        var detailOriginalBefore = detailKey.OriginalValue;
        var rootClrBefore = root.Id;
        var detailClrBefore = detail.Id;
        var rootTemporaryBefore = rootKey.IsTemporary;
        var detailTemporaryBefore = detailKey.IsTemporary;
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        rootUpdate.SetStoreGeneratedValue(rootKey.Metadata, 42, setModified: false);
        var propagated = detailUpdate.HasStoreGeneratedValue(detailKey.Metadata);
        rootUpdate.SetStoreGeneratedValue(
            rootEntry
                .Property(VersionProperty)
                .Metadata,
            new byte[] { 9 },
            setModified: false);

        detailUpdate.SetStoreGeneratedValue(
            detailEntry
                .Property(VersionProperty)
                .Metadata,
            new byte[] { 9 },
            setModified: false);

        // WHY: Acceptance is a real public EF lifecycle transition. Rollback must reinstate Added before
        // replaying temporary identities; normalizing this aggregate to Unchanged would reject valid state.
        context.ChangeTracker.AcceptAllChanges();
        var acceptedRoot = rootEntry.State;
        var acceptedDetail = detailEntry.State;

        // Act
        snapshot.Restore();

        // Assert
        Assert.True(rootTemporaryBefore);
        Assert.True(detailTemporaryBefore);
        Assert.True(propagated);
        Assert.Equal(EntityState.Unchanged, acceptedRoot);
        Assert.Equal(EntityState.Unchanged, acceptedDetail);
        Assert.Equal(EntityState.Added, rootEntry.State);
        Assert.Equal(EntityState.Added, detailEntry.State);
        Assert.Equal(rootBefore, rootKey.CurrentValue);
        Assert.Equal(detailBefore, detailKey.CurrentValue);
        Assert.Equal(rootOriginalBefore, rootKey.OriginalValue);
        Assert.Equal(detailOriginalBefore, detailKey.OriginalValue);
        Assert.Equal(rootClrBefore, root.Id);
        Assert.Equal(detailClrBefore, detail.Id);
        Assert.True(rootKey.IsTemporary);
        Assert.True(detailKey.IsTemporary);
        Assert.Same(detail, root.Detail);
        Assert.Same(root, detail.Root);
        AssertVersion(rootEntry);
        AssertVersion(detailEntry);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Accepted deletion of the complete sharing row restores both originally Deleted entries.</summary>
    /// <param name="owned">Whether dependent deletion follows ownership or ordinary cascade metadata.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptedDeletedSharingAggregateRemainsDeleted(
        bool owned
    )
    {
        // Arrange
        await using var context = owned
            ? new SharingContext()
            : new SplitSharingContext();

        var detail = new SharingDetail { Id = 7 };
        var root = new SharingRoot
        {
            Id = 7,
            Detail = detail,
        };

        detail.Root = root;
        context.Attach(root);
        var rootEntry = context.Entry(root);
        var detailEntry = context.Entry(detail);
        SeedVersion(rootEntry);
        SeedVersion(detailEntry);
        detail.Label = "Pending label";
        detail.Count = 8;
        context.Remove(root);
        var rootBefore = rootEntry.State;
        var detailBefore = detailEntry.State;
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        context.ChangeTracker.AcceptAllChanges();
        var acceptedRoot = rootEntry.State;
        var acceptedDetail = detailEntry.State;

        // Act
        snapshot.Restore();

        // Assert
        Assert.Equal(EntityState.Deleted, rootBefore);
        Assert.Equal(EntityState.Deleted, detailBefore);
        Assert.Equal(EntityState.Detached, acceptedRoot);
        Assert.Equal(EntityState.Detached, acceptedDetail);
        Assert.Equal(EntityState.Deleted, rootEntry.State);
        Assert.Equal(EntityState.Deleted, detailEntry.State);
        Assert.Equal(7, root.Id);
        Assert.Equal(7, detail.Id);
        Assert.Same(detail, root.Detail);
        Assert.Same(root, detail.Root);
        Assert.Equal("Pending label", detail.Label);
        Assert.Equal(
            "Stored label",
            detailEntry.Property(value => value.Label)
                .OriginalValue);

        Assert.Equal(8, detail.Count);
        Assert.Equal(
            1,
            detailEntry.Property(value => value.Count)
                .OriginalValue);

        AssertVersion(rootEntry);
        AssertVersion(detailEntry);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Obtains actual provider state without change detection or a standalone tracker.</summary>
    private static IUpdateEntry Update(
        DbContext context,
        object entity
    )
        => context
            .GetService<IUpdateAdapterFactory>()
            .Create()
            .Entries
            .Single(entry => ReferenceEquals(
                entry.ToEntityEntry()
                    .Entity,
                entity));

    /// <summary>Seeds the shared shadow token through public current/original APIs without a pending flag.</summary>
    private static void SeedVersion(
        EntityEntry entry
    )
    {
        var property = entry.Property(VersionProperty);
        byte[] value = [1];
        property.CurrentValue = value;
        property.OriginalValue = value;
        property.IsModified = false;
    }

    /// <summary>Reads exact flags independently of an entry's Modified state.</summary>
    private static string[] ModifiedProperties(
        IUpdateEntry entry
    )
        => entry
            .EntityType
            .GetProperties()
            .Where(entry.IsModified)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>Checks both aliases' current/original shadow token and its modification flag.</summary>
    private static void AssertVersion(
        EntityEntry entry
    )
    {
        var property = entry.Property(VersionProperty);
        Assert.Equal<byte>([1], Assert.IsType<byte[]>(property.CurrentValue));
        Assert.Equal<byte>([1], Assert.IsType<byte[]>(property.OriginalValue));
        Assert.False(property.IsModified);
    }
}

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies large tracked snapshots and exact mutable rollback values without database I/O.</summary>
public sealed partial class TrackerSnapshotTests
{
    /// <summary>Restores ordinary scalar values and entry flags across a complete large tracked unit of work.</summary>
    [Fact]
    public async Task Restore_TwentyThousandEntities_RetainsScalarValuesAndTrackedMembership()
    {
        // Arrange
        await using var context = new SnapshotContext();
        var entities = Enumerable
            .Range(1, 20_000)
            .Select(id => new SnapshotScalars { Id = id, Value01 = id, Value14 = -id })
            .ToArray();

        context.AttachRange(entities);
        var snapshot = NestedSetTrackerSnapshot.Capture(context);

        // Act
        foreach (var entity in entities)
        {
            entity.Value01 = -1;
            entity.Value14 = 1;
        }

        context.ChangeTracker.DetectChanges();
        snapshot.Restore();

        // Assert
        Assert.All(entities, entity =>
        {
            Assert.Equal(entity.Id, entity.Value01);
            Assert.Equal(-entity.Id, entity.Value14);
            var entry = context.Entry(entity);
            Assert.Equal(EntityState.Unchanged, entry.State);
            Assert.Equal(entity.Id, entry.Property(value => value.Value01).OriginalValue);
            Assert.Equal(-entity.Id, entry.Property(value => value.Value14).OriginalValue);
            Assert.All(entry.Properties, property => Assert.False(property.IsModified));
        });

        Assert.Equal(entities.Length, context.ChangeTracker.Entries().Count());
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Preserves an unchanged mutable value's pre-callback contents through snapshot restoration.</summary>
    [Fact]
    public async Task Restore_UnchangedMutablePayload_RetainsItsOwnedRollbackSnapshot()
    {
        // Arrange
        await using var context = new SnapshotContext();
        var entity = new SnapshotPayload
        {
            Id = 1,
            Values = [1, 2, 3],
        };

        context.Attach(entity);
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        entity.Values.Add(4);
        context.ChangeTracker.DetectChanges();

        // Act
        snapshot.Restore();

        // Assert
        Assert.Equal<int>([1, 2, 3], entity.Values);
        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(entity)
                .State);

        Assert.Equal<int>(
            [1, 2, 3],
            context
                .Entry(entity)
                .Property(value => value.Values)
                .OriginalValue);

        Assert.False(
            context
                .Entry(entity)
                .Property(value => value.Values)
                .IsModified);
    }
}

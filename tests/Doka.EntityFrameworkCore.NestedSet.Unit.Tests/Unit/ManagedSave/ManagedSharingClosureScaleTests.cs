using Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;
using Microsoft.EntityFrameworkCore.Update;
using SharingContext = Doka.EntityFrameworkCore.NestedSet.Tests.ManagedSharingCaptureTests.SharingContext;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks exact physical row-sharing closure beside unrelated tracked populations.</summary>
public sealed class ManagedSharingClosureScaleTests
{
    /// <summary>The exact selected physical-row closure excludes unrelated tracked rows at every population.</summary>
    /// <param name="sharing">Whether the actual callback write has same-row peers instead of a regular FK.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Observe_RowSharingClosure_ExcludesUnrelatedTrackedRows(
        bool sharing
    )
    {
        // Arrange
        await using var oneContext = new SharingContext();
        await using var mediumContext = new SharingContext();
        await using var largeContext = new SharingContext();
        var one = Prepare(oneContext, 1, sharing);
        var medium = Prepare(mediumContext, 128, sharing);
        var large = Prepare(largeContext, 2_048, sharing);
        var expectedCount = sharing
            ? 4
            : 1;

        // Act
        var oneObservation = Observe(oneContext, one);
        var mediumObservation = Observe(mediumContext, medium);
        var largeObservation = Observe(largeContext, large);

        // Assert
        Assert.Equal(expectedCount, oneObservation);
        Assert.Equal(expectedCount, mediumObservation);
        Assert.Equal(expectedCount, largeObservation);
        Assert.Equal(EntityState.Modified, one.EntityState);
        Assert.Equal(EntityState.Modified, medium.EntityState);
        Assert.Equal(EntityState.Modified, large.EntityState);
        Assert.True(
            one.IsModified(
                one.EntityType.FindProperty(nameof(ManagedSharingCaptureTests.SharingRoot.Payload))!));

        Assert.True(
            medium.IsModified(
                medium.EntityType.FindProperty(nameof(ManagedSharingCaptureTests.SharingRoot.Payload))!));

        Assert.True(
            large.IsModified(
                large.EntityType.FindProperty(nameof(ManagedSharingCaptureTests.SharingRoot.Payload))!));

        Assert.False(oneContext.ChangeTracker.AutoDetectChangesEnabled);
        Assert.False(mediumContext.ChangeTracker.AutoDetectChangesEnabled);
        Assert.False(largeContext.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            oneContext.Database.GetDbConnection()
                .State);

        Assert.Equal(
            ConnectionState.Closed,
            mediumContext.Database.GetDbConnection()
                .State);

        Assert.Equal(
            ConnectionState.Closed,
            largeContext.Database.GetDbConnection()
                .State);
    }

    /// <summary>Tracks independent rows of one mapping so table metadata cannot identify a whole tracker.</summary>
    private static IUpdateEntry Prepare(
        SharingContext context,
        int rows,
        bool sharing
    )
    {
        var roots = Enumerable
            .Range(1, rows)
            .Select(id => ManagedSharingCaptureTests.CreateRoot(id, derived: id % 2 == 0))
            .ToArray();

        context.AttachRange(roots);
        var selected = sharing
            ? (object)roots[0]
            : roots[0]
                .Related[0];

        var seed = ManagedSharingCaptureTests.Entry(context, selected);
        seed
            .ToEntityEntry()
            .Property(nameof(ManagedSharingCaptureTests.SharingRoot.Payload))
            .IsModified = true;

        return seed;
    }

    /// <summary>Observes the actual helper once using the independently selected native write.</summary>
    private static int Observe(
        SharingContext context,
        IUpdateEntry seed
    )
    {
        var callbacks = new List<IUpdateEntry>(4) { seed };
        NestedSetSaveChanges.IncludeManagedSharingPeers(context, callbacks);

        return callbacks.Count;
    }
}

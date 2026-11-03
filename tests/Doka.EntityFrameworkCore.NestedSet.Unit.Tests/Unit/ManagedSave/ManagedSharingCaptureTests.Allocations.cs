using Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;
using Microsoft.EntityFrameworkCore.Update;
using SharingContext = Doka.EntityFrameworkCore.NestedSet.Tests.ManagedSharingCaptureTests.SharingContext;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Separates row-sharing closure allocation from model setup and unrelated tracker population.</summary>
[Trait("Category", "Allocation")]
[Collection("Allocation measurements")]
public sealed class ManagedSharingCaptureAllocationTests
{
    private const int Repetitions = 100;
    private readonly ITestOutputHelper _output;

    /// <summary>Retains all observed allocation values in the test result for comparison.</summary>
    public ManagedSharingCaptureAllocationTests(
        ITestOutputHelper output
    )
    {
        _output = output;
    }

    /// <summary>Warmed closure allocation follows the selected physical row, not unrelated tracked rows.</summary>
    /// <param name="sharing">Whether the actual callback write has same-row peers instead of a regular FK.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WarmCaptureAllocationDoesNotGrowWithUnrelatedTrackedRows(
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
        var expectedCount = sharing ? 4 : 1;

        // Act
        var oneObservation = Observe(oneContext, one);
        var mediumObservation = Observe(mediumContext, medium);
        var largeObservation = Observe(largeContext, large);

        // Assert
        // WHY: The selected write and its exact row closure are identical at every tracker size. Allow only
        // fixed runtime headroom; materializing unrelated entries would add a per-tracker allocation slope.
        _output.WriteLine($"Sharing={sharing}; passes={Repetitions}; rows=1/128/2048; "
            + $"allocated={oneObservation.Allocated}/{mediumObservation.Allocated}/"
            + $"{largeObservation.Allocated} B.");
        Assert.InRange(oneObservation.Allocated, 0, Repetitions * 4_096L);
        Assert.InRange(mediumObservation.Allocated, 0, oneObservation.Allocated + 4_096L);
        Assert.InRange(largeObservation.Allocated, 0, oneObservation.Allocated + 4_096L);
        Assert.Equal(Repetitions * expectedCount, oneObservation.Captured);
        Assert.Equal(Repetitions * expectedCount, mediumObservation.Captured);
        Assert.Equal(Repetitions * expectedCount, largeObservation.Captured);
        Assert.Equal(EntityState.Modified, one.EntityState);
        Assert.Equal(EntityState.Modified, medium.EntityState);
        Assert.Equal(EntityState.Modified, large.EntityState);
        Assert.True(one.IsModified(
            one.EntityType.FindProperty(nameof(ManagedSharingCaptureTests.SharingRoot.Payload))!));
        Assert.True(medium.IsModified(
            medium.EntityType.FindProperty(nameof(ManagedSharingCaptureTests.SharingRoot.Payload))!));
        Assert.True(large.IsModified(
            large.EntityType.FindProperty(nameof(ManagedSharingCaptureTests.SharingRoot.Payload))!));
        Assert.False(oneContext.ChangeTracker.AutoDetectChangesEnabled);
        Assert.False(mediumContext.ChangeTracker.AutoDetectChangesEnabled);
        Assert.False(largeContext.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed,
            oneContext.Database.GetDbConnection().State);
        Assert.Equal(ConnectionState.Closed,
            mediumContext.Database.GetDbConnection().State);
        Assert.Equal(ConnectionState.Closed,
            largeContext.Database.GetDbConnection().State);
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
            : roots[0].Related[0];

        var seed = ManagedSharingCaptureTests.Entry(context, selected);
        seed
            .ToEntityEntry()
            .Property(nameof(ManagedSharingCaptureTests.SharingRoot.Payload))
            .IsModified = true;
        var callbacks = new List<IUpdateEntry>(4) { seed };

        // WHY: Close table metadata and native key/dependent lookups before charging repeated capture work.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < 10; index++)
        {
            callbacks.Clear();
            callbacks.Add(seed);
            NestedSetSaveChanges.IncludeManagedSharingPeers(context, callbacks);
        }

        return seed;
    }

    /// <summary>Measures only the existing helper; the callback list already has capacity for the exact row.</summary>
    private static (long Allocated, int Captured) Observe(
        SharingContext context,
        IUpdateEntry seed
    )
    {
        var callbacks = new List<IUpdateEntry>(4);
        var allocated = 0L;
        var captured = 0;

        // WHY: Reset the bounded input outside each measured interval so list setup is not assigned to discovery.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < Repetitions; index++)
        {
            callbacks.Clear();
            callbacks.Add(seed);
            var before = GC.GetAllocatedBytesForCurrentThread();
            NestedSetSaveChanges.IncludeManagedSharingPeers(context, callbacks);
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            captured += callbacks.Count;
        }

        return (allocated, captured);
    }
}

using Microsoft.EntityFrameworkCore.Update;
using ProbeOwner = Doka.EntityFrameworkCore.NestedSet.Tests.HierarchyChangeDetectionTests.ProbeOwner;
using SnapshotContext = Doka.EntityFrameworkCore.NestedSet.Tests.HierarchyChangeDetectionTests.SnapshotContext;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Bounds persisted-FK observation when the provider write set needs no relationship proof.</summary>
/// <param name="output">The result log receiving observations outside the measured interval.</param>
[Trait("Category", "Allocation")]
[Collection("Allocation measurements")]
public sealed class PersistedRelationshipProofAllocationTests(
    ITestOutputHelper output
)
{
    /// <summary>Skipped native write entries do not allocate a lambda capture for each row.</summary>
    /// <param name="nodes">The number of entries in the actual EF update adapter.</param>
    /// <param name="hierarchy">Whether entries are new hierarchy nodes or modified ordinary owners.</param>
    [Theory]
    [InlineData(1, false)]
    [InlineData(128, false)]
    [InlineData(8000, false)]
    [InlineData(1, true)]
    [InlineData(128, true)]
    [InlineData(8000, true)]
    public async Task SkippedWriteEntriesRetainFixedCaptureAllocation(
        int nodes,
        bool hierarchy
    )
    {
        // Arrange
        await using var context = new SnapshotContext();

        if (hierarchy)
        {
            var entities = Enumerable
                .Range(1, nodes)
                .Select(id => HierarchyChangeDetectionTests.CreateNode(id))
                .ToArray();

            await context.AddRangeAsync(entities, CancellationToken.None);
        }
        else
        {
            var owners = Enumerable
                .Range(1, nodes)
                .Select(id => new ProbeOwner { Id = id })
                .ToArray();

            context.AttachRange(owners);

            foreach (var owner in owners)
            {
                context
                    .Entry(owner)
                    .Property(value => value.Payload)
                    .CurrentValue = "written payload";
            }
        }

        var entries = context
            .GetService<IUpdateAdapterFactory>()
            .Create()
            .Entries
            .ToArray();

        // WHY: Warm the actual native-entry hook; model creation and tracking are outside the measured interval.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < 16; index++)
        {
            _ = NestedSetCollectionBaseline.CapturePersisted(entries);
        }

        const int repetitions = 10;
        var completions = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();

        // Act
        // WHY: Inspect only returned work during the fixed loop; assertion/output allocations stay outside it.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < repetitions; index++)
        {
            if (NestedSetCollectionBaseline.CapturePersisted(entries) is not null)
            {
                completions++;
            }
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        output.WriteLine(
            $"Persisted FK capture: nodes={nodes}; hierarchy={hierarchy}; passes={repetitions}; "
            + $"allocated={allocated} B; maximum=2048 B.");

        Assert.Equal(nodes, entries.Length);
        Assert.Equal(0, completions);
        Assert.InRange(allocated, 0, 2048);
        Assert.All(
            entries,
            entry => Assert.Equal(
                hierarchy
                    ? EntityState.Added
                    : EntityState.Modified,
                entry.EntityState));

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>An owner already in the insertion journal does not allocate another filtering closure.</summary>
    [Fact]
    public async Task KnownOwnerCaptureRetainsFixedWarmAllocation()
    {
        // Arrange
        await using var context = new SnapshotContext();
        var node = HierarchyChangeDetectionTests.CreateNode(2);
        var owner = new ProbeOwner { Id = node.Scope };
        context.Attach(owner);
        var entry = context.Attach(node);
        NestedSetCollectionBaseline? baseline = null;
        NestedSetCollectionBaseline.CaptureBeforeTracking(entry, ref baseline);
        var initial = baseline;
        var update = NestedSetInsertionContract.Instance.Entry(context.Entry(owner));
        var navigation = update.EntityType.FindNavigation(nameof(ProbeOwner.ScopeNodes))!;

        // WHY: Bind the existing journal operation once; native principal resolution is not this allocation probe.
        var capture = typeof(NestedSetCollectionBaseline).GetMethod(
                "Capture",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action<IUpdateEntry, INavigation, HashSet<object>?>>(baseline);

        var members = owner.ScopeNodes.ToArray();
        const int repetitions = 100;

        // WHY: Warm dictionary and delegate dispatch outside the measured steady-state loop.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < 16; index++)
        {
            capture(update, navigation, null);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        // Act
        // WHY: Measure the journal's real already-captured branch without wrapper or principal-lookup allocation.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < repetitions; index++)
        {
            capture(update, navigation, null);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        output.WriteLine($"Known owner capture: passes={repetitions}; allocated={allocated} B; maximum=0 B.");
        Assert.NotNull(initial);
        Assert.Same(initial, baseline);
        Assert.Equal(members, owner.ScopeNodes);
        Assert.Equal(0, allocated);
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }
}

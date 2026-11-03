using Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Isolates the warmed scalar probe from EF entry acquisition and runner allocation.</summary>
// WHY: This collection already excludes instrumentation and competing diagnostic observers from fixed budgets.
[Collection("Allocation measurements")]
public sealed class HierarchyChangeDetectionAllocationTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Preserves measured values in the test result for inspection beside the fixed budgets.</summary>
    public HierarchyChangeDetectionAllocationTests(
        ITestOutputHelper output
    )
    {
        _output = output;
    }

    /// <summary>Plan capture allocates for authorized candidates rather than every tracked clean hierarchy.</summary>
    /// <param name="nodes">The total tracked hierarchy entries scanned by later boundaries.</param>
    /// <param name="candidates">The original affected candidates whose scalar plans are captured.</param>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1024, 0)]
    [InlineData(1, 1)]
    [InlineData(1024, 1)]
    [InlineData(128, 64)]
    public async Task GuardPlanCaptureAllocationDependsOnCandidateCount(
        int nodes,
        int candidates
    )
    {
        // Arrange
        await using var context = new HierarchyChangeDetectionTests.SnapshotContext();
        var entities = Enumerable
            .Range(1, nodes)
            .Select(id => HierarchyChangeDetectionTests.CreateNode(id))
            .ToArray();

        context.AttachRange(entities);
        var entries = entities
            .Take(candidates)
            .Select(node => context.Entry(node))
            .Cast<EntityEntry>()
            .ToList();

        // WHY: Visit the retained candidate list directly; marking scalar intent never changes its membership.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < entries.Count; index++)
        {
            entries[index].Property(nameof(HierarchyChangeDetectionTests.ProbeNode.Name)).IsModified = true;
        }

        _ = HierarchyChangeDetectionTests.CapturePlan(entries);
        const int repetitions = 10;
        var observed = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();

        // Act
        // WHY: Measure only actual capture and its returned count, without reflective invocation or enumeration.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < repetitions; index++)
        {
            observed += HierarchyChangeDetectionTests.PlanCount(HierarchyChangeDetectionTests.CapturePlan(entries));
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        var maximum = 4096L + repetitions * candidates * 1600L;
        _output.WriteLine($"Guard capture: nodes={nodes}; candidates={candidates}; passes={repetitions}; "
            + $"allocated={allocated} B; maximum={maximum} B.");
        Assert.InRange(allocated, 0, maximum);
        Assert.Equal(repetitions * candidates, observed);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }

    /// <summary>Repeated exact-plan checks avoid scalar boxes and wrappers even with many clean entries.</summary>
    [Fact]
    public async Task GuardPlanValidationKeepsFixedWarmAllocation()
    {
        // Arrange
        await using var context = new HierarchyChangeDetectionTests.SnapshotContext();
        var entities = Enumerable
            .Range(1, 1024)
            .Select(id => HierarchyChangeDetectionTests.CreateNode(id))
            .ToArray();

        context.AttachRange(entities);
        var entry = context.Entry(entities[0]);
        entry.Property(value => value.Name).IsModified = true;
        var plan = HierarchyChangeDetectionTests.CapturePlan([entry]);
        const int repetitions = 10;
        var changes = 0;

        // WHY: Warm metadata and typed readers before measuring the existing plan's complete boundary scan.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < repetitions; index++)
        {
            changes += HierarchyChangeDetectionTests.PlanHasChanges(context, plan) ? 1 : 0;
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        // Act
        // WHY: Keep test iteration outside collection enumeration so the budget covers the actual observer.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < repetitions; index++)
        {
            changes += HierarchyChangeDetectionTests.PlanHasChanges(context, plan) ? 1 : 0;
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        _output.WriteLine($"Guard validation: nodes=1024; candidates=1; passes={repetitions}; "
            + $"allocated={allocated} B.");
        Assert.InRange(allocated, 0, 4096);
        Assert.Equal(0, changes);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }

    /// <summary>Unchanged tracked hierarchies do not introduce allocation for each node or scalar comparison.</summary>
    /// <param name="nodes">The tracked hierarchy size, including a wide unchanged set.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(128)]
    public async Task UnchangedHierarchyProbeHasBoundedTraversalAllocation(
        int nodes
    )
    {
        // Arrange
        await using var context = new HierarchyChangeDetectionTests.SnapshotContext();
        context.AttachRange(Enumerable
            .Range(1, nodes)
            .Select(id => HierarchyChangeDetectionTests.CreateNode(id)));
        const int repetitions = 10;
        var changes = 0;

        // WHY: Warm all metadata and typed comparisons without replacing ordinary empty navigation collections.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < repetitions; index++)
        {
            changes += NestedSetSaveChanges.HasHierarchyChanges(context) ? 1 : 0;
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        // Act
        // WHY: A counted loop excludes test enumeration from the full hierarchy-probe allocation budget.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < repetitions; index++)
        {
            changes += NestedSetSaveChanges.HasHierarchyChanges(context) ? 1 : 0;
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        _output.WriteLine($"Hierarchy probe: nodes={nodes}; passes={repetitions}; allocated={allocated} B.");
        // WHY: Fixed per-call adapter/enumerator bookkeeping fits; per-node boxes or iterators do not.
        Assert.InRange(allocated, 0, 4096);
        Assert.Equal(0, changes);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }

    /// <summary>Records native set-comparison enumeration cost for actual populated navigation collections.</summary>
    /// <param name="parents">The independently rooted parents whose collections each contain one child.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(64)]
    public async Task PopulatedHierarchyProbeHasExplicitEnumerationBudget(
        int parents
    )
    {
        // Arrange
        await using var context = new HierarchyChangeDetectionTests.SnapshotContext();

        // WHY: Seed paired parent/child identities and navigations before measuring native set comparison.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < parents; index++)
        {
            var parent = HierarchyChangeDetectionTests.CreateNode(index * 2 + 1);
            parent.TreeId = Guid.NewGuid();
            parent.Right = 4;
            var child = HierarchyChangeDetectionTests.CreateNode(index * 2 + 2);
            child.TreeId = parent.TreeId;
            child.ParentId = parent.Id;
            child.Parent = parent;
            child.Left = 2;
            child.Right = 3;
            child.Depth = 1;
            parent.Children!.Add(child);
            context.Attach(parent);
        }

        const int repetitions = 10;
        var changes = 0;

        // WHY: Warm actual populated collections; replacing them with empty/null collections hides their cost.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < repetitions; index++)
        {
            changes += NestedSetSaveChanges.HasHierarchyChanges(context) ? 1 : 0;
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        // Act
        // WHY: Keep test iteration allocation-free so the measured increment belongs to native set comparison.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < repetitions; index++)
        {
            changes += NestedSetSaveChanges.HasHierarchyChanges(context) ? 1 : 0;
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        // WHY: HashSet.SetEquals currently allocates a 40-byte enumerator for each nonempty ObservableCollection.
        // Preserve EF reference-set/duplicate semantics; 48 bytes permits limited headroom without hiding growth.
        var maximum = 4096L + repetitions * parents * 48L;
        _output.WriteLine($"Populated hierarchy probe: parents={parents}; nodes={parents * 2}; passes={repetitions}; "
            + $"allocated={allocated} B; maximum={maximum} B.");
        Assert.InRange(allocated, 0, maximum);
        Assert.Equal(0, changes);
        Assert.Equal(parents * 2,
            context
                .ChangeTracker
                .Entries()
                .Count());
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }

    /// <summary>An unchanged Int64 comparison does not box its current and original value on each probe.</summary>
    [Fact]
    public async Task UnchangedValueTypeComparisonDoesNotAllocatePerProbe()
    {
        // Arrange
        await using var context = new HierarchyChangeDetectionTests.SnapshotContext();
        var node = HierarchyChangeDetectionTests.CreateNode();
        context.Attach(node);
        var entry = HierarchyChangeDetectionTests.UpdateEntry(context, node);
        var property = entry.EntityType.FindProperty(nameof(HierarchyChangeDetectionTests.ProbeNode.Left))!;
        const int repetitions = 10_000;
        var changes = 0;

        // WHY: The warmup closes the typed reader once; the measured loop must exclude metadata and entry setup.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < repetitions; index++)
        {
            changes += NestedSetTrackedProperty.HasChanged(entry, property) ? 1 : 0;
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        // Act
        // WHY: Index iteration keeps this budget specific to scalar probing instead of enumerator allocation.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < repetitions; index++)
        {
            changes += NestedSetTrackedProperty.HasChanged(entry, property) ? 1 : 0;
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        _output.WriteLine($"Typed scalar probe: passes={repetitions}; allocated={allocated} B.");
        // WHY: A fixed allowance admits runtime bookkeeping while rejecting two boxed Int64 values per probe.
        Assert.InRange(allocated, 0, 4096);
        Assert.Equal(0, changes);
        Assert.Equal(EntityState.Unchanged, entry.EntityState);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }
}

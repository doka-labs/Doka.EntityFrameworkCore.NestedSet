using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Bounds complete FK capture independently of the number of aliases to one shared owner collection.</summary>
[Trait("Category", "Allocation")]
[Collection("Allocation measurements")]
public sealed class UnchangedForeignKeyRollbackAllocationTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Retains actual capture allocation beside the fixed linear budget.</summary>
    public UnchangedForeignKeyRollbackAllocationTests(
        ITestOutputHelper output
    )
    {
        _output = output;
    }

    /// <summary>Capturing one or every entry copies each affected wide owner graph only once.</summary>
    /// <param name="nodes">The preexisting tracked width of the shared ordinary owner.</param>
    /// <param name="allEntries">Whether the snapshot owns all tracked entries or only one selected node.</param>
    /// <param name="warmNativeMaps">Whether EF's lazy native map was already initialized before measurement.</param>
    [Theory]
    [InlineData(1, false, true)]
    [InlineData(1, true, true)]
    [InlineData(128, false, true)]
    [InlineData(128, true, true)]
    [InlineData(2048, false, true)]
    [InlineData(2048, true, true)]
    [InlineData(20000, false, true)]
    [InlineData(20000, true, true)]
    [InlineData(128, false, false)]
    [InlineData(2048, false, false)]
    [InlineData(20000, false, false)]
    public async Task CompleteForeignKeyCaptureHasLinearAllocation(
        int nodes,
        bool allEntries,
        bool warmNativeMaps
    )
    {
        // Arrange
        await WarmCaptureMetadataAsync();
        await using var context = new HierarchyChangeDetectionTests.SnapshotContext();
        var owner = new HierarchyChangeDetectionTests.ProbeOwner { Id = 7 };
        var entities = Enumerable
            .Range(1, nodes)
            .Select(id =>
            {
                var node = HierarchyChangeDetectionTests.CreateNode(id);
                node.TreeId = Guid.NewGuid();

                return node;
            })
            .ToArray();

        foreach (var entity in entities)
        {
            owner.ScopeNodes.Add(entity);
        }

        context.Attach(owner);
        var tracked = context
            .GetService<IUpdateAdapterFactory>()
            .Create()
            .Entries
            .ToArray();

        var entries = allEntries
            ? tracked
            : tracked
                .Where(entry => ReferenceEquals(entry.ToEntityEntry().Entity,
                    entities[0]))
                .ToArray();

        var detections = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detections++;
        if (warmNativeMaps)
        {
            _ = NestedSetTrackerSnapshot.Capture(context, entries);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        // Act
        var snapshot = NestedSetTrackerSnapshot.Capture(context, entries);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        // WHY: Scalar/FK snapshots scale with captured entries; one shared owner's ordered CLR/native
        // arrays scale with width once. Repeating those arrays per dependent would exceed this linear bound.
        // WHY: First capture also lets EF initialize its own indexed dependent cache once. Its distinct
        // linear bound does not raise the existing warmed snapshot budget or permit per-owner rescans.
        var maximum = warmNativeMaps
            ? 8192L + (allEntries ? nodes * 6000L : nodes * 32L)
            : 8192L + nodes * 512L;

        _output.WriteLine($"Complete FK capture: nodes={nodes}; entries={entries.Length}; all={allEntries}; "
            + $"warm={warmNativeMaps}; allocated={allocated} B; maximum={maximum} B.");
        Assert.InRange(allocated, 1, maximum);
        Assert.Equal(allEntries ? nodes + 1 : 1, entries.Length);
        Assert.Equal(nodes, owner.ScopeNodes.Count);
        Assert.Equal(0, detections);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
        GC.KeepAlive(snapshot);
    }

    /// <summary>Warms process/model capture metadata without initializing the measured context's native map.</summary>
    private static async Task WarmCaptureMetadataAsync()
    {
        // WHY: First-use shared metadata costs depend on test order. A separate disposed fixture warms
        // that infrastructure while every measured context retains its own cold native dependent map.
        await using var context = new HierarchyChangeDetectionTests.SnapshotContext();
        var owner = new HierarchyChangeDetectionTests.ProbeOwner { Id = 7 };
        var node = HierarchyChangeDetectionTests.CreateNode(1);
        owner.ScopeNodes.Add(node);
        context.Attach(owner);
        var entry = context
            .GetService<IUpdateAdapterFactory>()
            .Create()
            .Entries
            .Single(candidate => ReferenceEquals(candidate.ToEntityEntry().Entity,
                node));

        GC.KeepAlive(NestedSetTrackerSnapshot.Capture(context, [entry]));
    }
}

using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks complete FK capture independently of aliases to one shared owner collection.</summary>
public sealed class ForeignKeyCaptureScaleTests
{
    /// <summary>Capturing one or every entry preserves the affected owner and all original members.</summary>
    /// <param name="nodes">The preexisting tracked width of the shared ordinary owner.</param>
    /// <param name="allEntries">Whether the snapshot owns all tracked entries or only one selected node.</param>
    /// <param name="warmNativeMaps">Whether EF's lazy native map was already initialized before the asserted capture.</param>
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
    public async Task Capture_CompleteForeignKeyCapture_PreservesSharedOwnerState(
        int nodes,
        bool allEntries,
        bool warmNativeMaps
    )
    {
        // Arrange
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
                .Where(entry => ReferenceEquals(
                    entry.ToEntityEntry()
                        .Entity,
                    entities[0]))
                .ToArray();

        var detections = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detections++;

        if (warmNativeMaps)
        {
            _ = NestedSetTrackerSnapshot.Capture(context, entries);
        }

        // Act
        var snapshot = NestedSetTrackerSnapshot.Capture(context, entries);

        // Assert
        Assert.NotNull(snapshot);
        Assert.Equal(
            allEntries
                ? nodes + 1
                : 1,
            entries.Length);

        Assert.Equal(nodes, owner.ScopeNodes.Count);
        Assert.Equal(0, detections);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }
}

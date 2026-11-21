using Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks exact candidate authorization and unchanged native hierarchy traversal at varied scales.</summary>
public sealed class HierarchyGuardScaleTests
{
    /// <summary>Only explicitly selected candidates belong to the captured guard plan.</summary>
    /// <param name="nodes">The complete tracked hierarchy population.</param>
    /// <param name="candidates">The independently selected authorized candidates.</param>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1024, 0)]
    [InlineData(1, 1)]
    [InlineData(1024, 1)]
    [InlineData(128, 64)]
    public async Task PlanCount_GuardPlanCapture_PreservesExactCandidateSet(
        int nodes,
        int candidates
    )
    {
        // Arrange
        await using var context = new HierarchyChangeDetectionTests.SnapshotContext();
        var entities = Enumerable
            .Range(1, nodes)
            .Select(HierarchyChangeDetectionTests.CreateNode)
            .ToArray();
        context.AttachRange(entities);
        var entries = entities
            .Take(candidates)
            .Select(node => (EntityEntry)context.Entry(node))
            .ToList();

        foreach (var entry in entries)
        {
            entry.Property(nameof(HierarchyChangeDetectionTests.ProbeNode.Name)).IsModified = true;
        }

        // Act
        var plan = HierarchyChangeDetectionTests.CapturePlan(entries);

        // Assert
        Assert.Equal(candidates, HierarchyChangeDetectionTests.PlanCount(plan));
        Assert.False(HierarchyChangeDetectionTests.PlanHasChanges(context, plan));
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>One authorized plan remains valid beside many unchanged native identities.</summary>
    [Fact]
    public async Task PlanHasChanges_GuardPlanValidation_RetainsExactAuthorization()
    {
        // Arrange
        await using var context = new HierarchyChangeDetectionTests.SnapshotContext();
        var entities = Enumerable
            .Range(1, 1024)
            .Select(HierarchyChangeDetectionTests.CreateNode)
            .ToArray();
        context.AttachRange(entities);
        var entry = context.Entry(entities[0]);
        entry.Property(value => value.Name).IsModified = true;
        var plan = HierarchyChangeDetectionTests.CapturePlan([entry]);

        // Act
        var changed = HierarchyChangeDetectionTests.PlanHasChanges(context, plan);

        // Assert
        Assert.False(changed);
        Assert.Equal(1, HierarchyChangeDetectionTests.PlanCount(plan));
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>All unchanged native identities preserve their initial structural values.</summary>
    /// <param name="nodes">The complete tracked hierarchy population.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(128)]
    public async Task HasHierarchyChanges_UnchangedHierarchyProbe_PreservesNativeState(
        int nodes
    )
    {
        // Arrange
        await using var context = new HierarchyChangeDetectionTests.SnapshotContext();
        context.AttachRange(Enumerable.Range(1, nodes).Select(HierarchyChangeDetectionTests.CreateNode));

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.False(changed);
        Assert.Equal(
            nodes,
            context.ChangeTracker.Entries()
                .Count());
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Populated parent collections retain reference-set semantics without structural writes.</summary>
    /// <param name="parents">The independent parents, each owning exactly one child.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(64)]
    public async Task HasHierarchyChanges_PopulatedHierarchyProbe_PreservesNativeRelationships(
        int parents
    )
    {
        // Arrange
        await using var context = new HierarchyChangeDetectionTests.SnapshotContext();

        // WHY: Each pair needs distinct identities but one shared tree within that pair.
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

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.False(changed);
        Assert.Equal(
            parents * 2,
            context.ChangeTracker.Entries()
                .Count());
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>A typed unchanged scalar retains its native current/original equality.</summary>
    [Fact]
    public async Task HasChanged_UnchangedValueTypeComparison_PreservesNativeEquality()
    {
        // Arrange
        await using var context = new HierarchyChangeDetectionTests.SnapshotContext();
        var node = HierarchyChangeDetectionTests.CreateNode();
        context.Attach(node);
        var entry = HierarchyChangeDetectionTests.UpdateEntry(context, node);
        var property = entry.EntityType.FindProperty(nameof(HierarchyChangeDetectionTests.ProbeNode.Left))!;

        // Act
        var changed = NestedSetTrackedProperty.HasChanged(entry, property);

        // Assert
        Assert.False(changed);
        Assert.Equal(EntityState.Unchanged, entry.EntityState);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }
}

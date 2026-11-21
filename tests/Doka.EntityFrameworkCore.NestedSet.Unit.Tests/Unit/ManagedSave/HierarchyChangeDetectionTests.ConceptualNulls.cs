using Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class HierarchyChangeDetectionTests
{
    /// <summary>Shadow values use their tracked storage rather than requiring a CLR property getter.</summary>
    /// <param name="change">Whether a shadow scalar changes after the original value has been captured.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HasChanged_ShadowValues_RespectTrackedModificationState(
        bool change
    )
    {
        // Arrange
        await using var context = new SnapshotContext();
        var owner = new ProbeOwner { Id = 9 };
        context.Attach(owner);
        var property = context
            .Entry(owner)
            .Property<int>("ShadowOrder");

        if (change)
        {
            property.CurrentValue = 2;
        }

        var update = UpdateEntry(context, owner);

        // Act
        var changed = NestedSetTrackedProperty.HasChanged(update, property.Metadata);

        // Assert
        Assert.Equal(change, changed);
        Assert.Equal(change, property.IsModified);
    }

    /// <summary>Null comparison follows EF's object-comparer guard without calling custom equality on nulls.</summary>
    /// <param name="original">The value captured when tracking begins.</param>
    /// <param name="current">The unmarked CLR value observed by the scalar probe.</param>
    /// <param name="expected">Whether EF's null-safe equality considers the values different.</param>
    [Theory]
    [InlineData(null, null, false)]
    [InlineData(null, "Alpha", true)]
    [InlineData("Alpha", null, true)]
    [InlineData("Alpha", "Alpha", false)]
    [InlineData("Alpha", "Bravo", true)]
    public async Task HasChanged_NullableCustomComparer_IsNotInvokedWithNullValues(
        string? original,
        string? current,
        bool expected
    )
    {
        // Arrange
        await using var context = new SnapshotContext();
        var owner = new ProbeOwner
        {
            Id = 9,
            StrictText = original,
        };

        context.Attach(owner);
        var entry = context.Entry(owner);
        var property = entry.Property(value => value.StrictText);
        owner.StrictText = current;
        var update = UpdateEntry(context, owner);

        // Act
        var changed = NestedSetTrackedProperty.HasChanged(update, property.Metadata);

        // Assert
        Assert.Equal(expected, changed);
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.False(property.IsModified);
    }

    /// <summary>Conceptual nulls remain structural work even when EF retains the non-null CLR sentinel.</summary>
    [Fact]
    public async Task HasHierarchyChanges_ConceptualNullRole_IsObservedWithoutReplacingClrValue()
    {
        // Arrange
        await using var context = new SnapshotContext();
        var node = CreateNode();
        context.Attach(node);

        // WHY: Retain EF's conceptual-null state for inspection instead of immediately processing the orphan.
        context.ChangeTracker.DeleteOrphansTiming = CascadeTiming.Never;
        var property = context
            .Entry(node)
            .Property(nameof(ProbeNode.Scope));

        // WHY: EF records null for this required scalar in its conceptual-null flag instead of an Int32 field.
        property.CurrentValue = null;
        var update = UpdateEntry(context, node);

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.True(changed);
        Assert.True(update.IsConceptualNull(property.Metadata));
        Assert.Equal(7, node.Scope);
    }

    /// <summary>A conceptual null remains visible when flags and all scalar snapshots otherwise agree.</summary>
    [Fact]
    public async Task HasChanged_ConceptualNullWithoutModifiedFlag_IsStillHierarchyWork()
    {
        // Arrange
        await using var context = new SnapshotContext();
        var node = CreateNode();
        context.Attach(node);
        context.ChangeTracker.DeleteOrphansTiming = CascadeTiming.Never;
        var property = context
            .Entry(node)
            .Property(value => value.Scope)
            .Metadata;

        var update = UpdateEntry(context, node);

        // WHY: The existing framework seam sets setModified=false, unlike PropertyEntry.CurrentValue. This
        // isolates conceptual null from equal CLR/original/relationship values and independent Modified flags.
        NestedSetInsertionContract.Instance.SetProperty(update, property, null);

        // Act
        var scalarChanged = NestedSetTrackedProperty.HasChanged(update, property);
        var hierarchyChanged = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.True(scalarChanged);
        Assert.True(hierarchyChanged);
        Assert.True(update.IsConceptualNull(property));
        Assert.False(update.IsModified(property));
        Assert.Equal(7, update.GetCurrentValue<int>(property));
        Assert.Equal(7, update.GetOriginalValue<int>(property));
        Assert.Equal(7, update.GetRelationshipSnapshotValue(property));
        Assert.Equal(EntityState.Unchanged, update.EntityState);
    }
}

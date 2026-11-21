using Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class HierarchyChangeDetectionTests
{
    /// <summary>Detached candidates cannot be replaced with an equal-key object to reuse their authorization.</summary>
    [Fact]
    public async Task PlanHasChanges_AuthorizedCandidate_RequiresExactTrackedEntry()
    {
        // Arrange
        await using var context = new SnapshotContext();
        var entry = context.Attach(CreateNode());
        entry.Property(value => value.Name)
            .IsModified = true;

        var plan = CapturePlan([entry]);
        entry.State = EntityState.Detached;
        var replacement = CreateNode();
        context.Attach(replacement);

        // Act
        var changed = PlanHasChanges(context, plan);

        // Assert
        Assert.True(changed);
        Assert.Equal(EntityState.Detached, entry.State);
        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(replacement)
                .State);

        Assert.Equal(1, PlanCount(plan));
    }

    /// <summary>A suppressed Parent-only candidate cannot be deleted while all captured flags remain false.</summary>
    [Fact]
    public async Task PlanHasChanges_AuthorizedUnchangedCandidate_RejectsDeletion()
    {
        // Arrange
        await using var context = new SnapshotContext();
        var node = CreateNode(2);
        node.ParentId = 1;
        var entry = context.Attach(node);
        node.ParentId = 3;
        entry.Property(value => value.ParentId)
            .IsModified = true;

        entry.Property(value => value.ParentId)
            .IsModified = false;

        var plan = CapturePlan([entry]);
        entry.State = EntityState.Deleted;

        // Act
        var changed = PlanHasChanges(context, plan);

        // Assert
        Assert.True(changed);
        Assert.Equal(EntityState.Deleted, entry.State);
        Assert.Equal(1, node.ParentId);
        Assert.Equal(1, PlanCount(plan));
        Assert.False(
            entry.Property(value => value.ParentId)
                .IsModified);

        Assert.False(
            entry.Property(value => value.Name)
                .IsModified);

        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Shadow role changes are read through EF rather than through an unrelated CLR member.</summary>
    [Fact]
    public async Task PlanHasChanges_AuthorizedCandidate_RejectsUnmarkedShadowRoleChange()
    {
        // Arrange
        await using var context = new ShadowPositionPlanContext();
        var entry = context.Attach(CreateNode());
        entry.Property(value => value.Name)
            .IsModified = true;

        var property = entry
            .Property<long>("ShadowPosition")
            .Metadata;

        var update = UpdateEntry(context, entry.Entity);
        var plan = CapturePlan([entry]);
        NestedSetInsertionContract.Instance.SetProperty(update, property, 7L);

        // Act
        var changed = PlanHasChanges(context, plan);

        // Assert
        Assert.True(changed);
        Assert.Equal(
            7L,
            entry.Property<long>("ShadowPosition")
                .CurrentValue);

        Assert.False(
            entry.Property<long>("ShadowPosition")
                .IsModified);

        Assert.Equal(0, entry.Entity.Position);
    }

    /// <summary>The candidate snapshot reads provider-generated sidecars independently of the CLR key.</summary>
    [Fact]
    public async Task PlanHasChanges_AuthorizedCandidate_RejectsChangedGeneratedSidecar()
    {
        // Arrange
        await using var context = new GeneratedNodePlanContext();
        var entry = context.Attach(CreateNode());
        entry.Property(value => value.Name)
            .IsModified = true;

        var update = UpdateEntry(context, entry.Entity);
        var property = entry
            .Property(value => value.Id)
            .Metadata;

        update.SetStoreGeneratedValue(property, 1, setModified: false);
        var plan = CapturePlan([entry]);
        update.SetStoreGeneratedValue(property, 2, setModified: false);

        // Act
        var changed = PlanHasChanges(context, plan);

        // Assert
        Assert.True(changed);
        Assert.Equal(1, entry.Entity.Id);
        Assert.Equal(2, update.GetCurrentValue<int>(property));
        Assert.False(update.IsModified(property));
    }

    /// <summary>A generated sidecar cannot hide a later raw CLR key edit from the authorized plan.</summary>
    [Fact]
    public async Task PlanHasChanges_AuthorizedCandidate_RejectsClrKeyChangeBehindGeneratedSidecar()
    {
        // Arrange
        await using var context = new GeneratedNodePlanContext();
        var entry = context.Attach(CreateNode());
        entry.Property(value => value.Name)
            .IsModified = true;

        var update = UpdateEntry(context, entry.Entity);
        var property = entry
            .Property(value => value.Id)
            .Metadata;

        update.SetStoreGeneratedValue(property, 1, setModified: false);
        var plan = CapturePlan([entry]);
        entry.Entity.Id = 2;

        // Act
        var changed = PlanHasChanges(context, plan);

        // Assert
        Assert.Equal(1, update.GetCurrentValue<int>(property));
        Assert.Equal(2, entry.Entity.Id);
        Assert.False(update.IsModified(property));
        Assert.True(changed);
    }

    /// <summary>Initially different CLR and generated values remain valid when neither value changes.</summary>
    [Fact]
    public async Task PlanHasChanges_AuthorizedCandidate_PreservesCapturedGeneratedDivergence()
    {
        // Arrange
        await using var context = new GeneratedNodePlanContext();
        var entry = context.Attach(CreateNode());
        entry.Property(value => value.Name)
            .IsModified = true;

        var update = UpdateEntry(context, entry.Entity);
        var property = entry
            .Property(value => value.Id)
            .Metadata;

        update.SetStoreGeneratedValue(property, 2, setModified: false);
        var plan = CapturePlan([entry]);

        // Act
        var changed = PlanHasChanges(context, plan);

        // Assert
        Assert.False(changed);
        Assert.Equal(1, entry.Entity.Id);
        Assert.Equal(2, update.GetCurrentValue<int>(property));
        Assert.False(update.IsModified(property));
    }

    /// <summary>The ordinary observer also detects raw CLR keys hidden behind an unchanged generated sidecar.</summary>
    [Fact]
    public async Task HasHierarchyChanges_OrdinaryObserver_RejectsClrKeyChangeBehindGeneratedSidecar()
    {
        // Arrange
        await using var context = new GeneratedNodePlanContext();
        var node = CreateNode();
        var entry = context.Attach(node);
        var update = UpdateEntry(context, node);
        var property = entry
            .Property(value => value.Id)
            .Metadata;

        update.SetStoreGeneratedValue(property, 1, setModified: false);
        node.Id = 2;

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.Equal(1, update.GetCurrentValue<int>(property));
        Assert.Equal(1, update.GetOriginalValue<int>(property));
        Assert.Equal(2, node.Id);
        Assert.False(update.IsModified(property));
        Assert.True(changed);
    }
}

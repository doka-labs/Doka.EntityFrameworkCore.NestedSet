namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class HierarchyChangeDetectionTests
{
    /// <summary>A Parent-only candidate remains authorized after suppression makes it Unchanged.</summary>
    [Fact]
    public async Task PlanHasChanges_SuppressedParentOnlyCandidate_RetainsExactAuthorization()
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

        // Act
        var changed = PlanHasChanges(context, plan);

        // Assert
        Assert.False(changed);
        Assert.Equal(1, PlanCount(plan));
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.Equal(1, node.ParentId);
        Assert.False(
            entry.Property(value => value.ParentId)
                .IsModified);

        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Every raw role remains guarded even when a different property authorized the candidate.</summary>
    /// <param name="propertyName">The otherwise unmarked structural or ordering property changed by a callback.</param>
    [Theory]
    [InlineData(nameof(ProbeNode.Id))]
    [InlineData(nameof(ProbeNode.Scope))]
    [InlineData(nameof(ProbeNode.TreeId))]
    [InlineData(nameof(ProbeNode.ParentId))]
    [InlineData(nameof(ProbeNode.Left))]
    [InlineData(nameof(ProbeNode.Right))]
    [InlineData(nameof(ProbeNode.Depth))]
    [InlineData(nameof(ProbeNode.Position))]
    [InlineData(nameof(ProbeNode.Name))]
    public async Task PlanHasChanges_AuthorizedCandidate_RejectsUnmarkedRawRoleChanges(
        string propertyName
    )
    {
        // Arrange
        await using var context = new SnapshotContext();
        var node = CreateNode();
        var entry = context.Attach(node);
        entry.Property(value => value.Name)
            .IsModified = true;

        var plan = CapturePlan([entry]);
        ChangeRole(node, propertyName);

        // Act
        var changed = PlanHasChanges(context, plan);

        // Assert
        Assert.True(changed);
        Assert.Equal(
            propertyName == nameof(ProbeNode.Name),
            entry.Property(propertyName)
                .IsModified);

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Clearing an authorized flag cannot be hidden by equal current and original scalar values.</summary>
    [Fact]
    public async Task PlanHasChanges_AuthorizedCandidate_RejectsClearedOrderingFlag()
    {
        // Arrange
        await using var context = new SnapshotContext();
        var entry = context.Attach(CreateNode());
        entry.Property(value => value.Name)
            .IsModified = true;

        entry.Property(value => value.Payload)
            .IsModified = true;

        var plan = CapturePlan([entry]);
        entry.Property(value => value.Name)
            .IsModified = false;

        // Act
        var changed = PlanHasChanges(context, plan);

        // Assert
        Assert.True(changed);
        Assert.Equal(EntityState.Modified, entry.State);
        Assert.Equal(
            entry.Property(value => value.Name)
                .OriginalValue,
            entry.Entity.Name);

        Assert.False(
            entry.Property(value => value.Name)
                .IsModified);
    }

    /// <summary>A newly introduced managed flag is rejected even when its scalar value has not changed.</summary>
    [Fact]
    public async Task PlanHasChanges_AuthorizedCandidate_RejectsIntroducedStructuralFlag()
    {
        // Arrange
        await using var context = new SnapshotContext();
        var entry = context.Attach(CreateNode());
        entry.Property(value => value.Name)
            .IsModified = true;

        var plan = CapturePlan([entry]);
        entry.Property(value => value.Depth)
            .IsModified = true;

        // Act
        var changed = PlanHasChanges(context, plan);

        // Assert
        Assert.True(changed);
        Assert.Equal(0, entry.Entity.Depth);
        Assert.Equal(
            0,
            entry.Property(value => value.Depth)
                .OriginalValue);
    }

    /// <summary>Ordinary payload can change a candidate's state without changing its hierarchy plan.</summary>
    [Fact]
    public async Task PlanHasChanges_AuthorizedUnchangedCandidate_AllowsOrdinaryPayloadFlags()
    {
        // Arrange
        await using var context = new SnapshotContext();
        var entry = context.Attach(CreateNode());
        var plan = CapturePlan([entry]);
        entry.Entity.Payload = "Callback payload";
        entry.Property(value => value.Payload)
            .IsModified = true;

        // Act
        var changed = PlanHasChanges(context, plan);

        // Assert
        Assert.False(changed);
        Assert.Equal(EntityState.Modified, entry.State);
        Assert.True(
            entry.Property(value => value.Payload)
                .IsModified);

        Assert.False(
            entry.Property(value => value.Name)
                .IsModified);
    }

}

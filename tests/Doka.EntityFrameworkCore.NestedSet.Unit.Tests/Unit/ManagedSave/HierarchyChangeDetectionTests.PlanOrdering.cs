namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class HierarchyChangeDetectionTests
{
    /// <summary>Original-value tampering is visible independently of current values and modification flags.</summary>
    [Fact]
    public async Task PlanHasChanges_AuthorizedCandidate_RejectsChangedOrderingOriginal()
    {
        // Arrange
        await using var context = new SnapshotContext();
        var entry = context.Attach(CreateNode());
        entry.Property(value => value.Name)
            .IsModified = true;

        var plan = CapturePlan([entry]);
        entry.Property(value => value.Name)
            .OriginalValue = "Different original";

        // Act
        var changed = PlanHasChanges(context, plan);

        // Assert
        Assert.True(changed);
        Assert.Equal("Alpha", entry.Entity.Name);
        Assert.True(
            entry.Property(value => value.Name)
                .IsModified);
    }

    /// <summary>Unmarked secondary ordering criteria remain guarded beside an explicitly modified first one.</summary>
    [Fact]
    public async Task PlanHasChanges_AuthorizedCandidate_RejectsUnmarkedSecondaryOrderChange()
    {
        // Arrange
        await using var context = new SecondaryOrderingPlanContext();
        var entry = context.Attach(CreateNode());
        entry.Property(value => value.Name)
            .IsModified = true;

        var plan = CapturePlan([entry]);
        entry.Entity.Payload = "Changed secondary order";

        // Act
        var changed = PlanHasChanges(context, plan);

        // Assert
        Assert.True(changed);
        Assert.False(
            entry.Property(value => value.Payload)
                .IsModified);
    }

    /// <summary>Notification strategies without an ordering original still guard its current value.</summary>
    /// <param name="change">Whether the current value changes after the candidate snapshot.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlanHasChanges_AuthorizedNotificationCandidate_DoesNotRequireOrderingOriginal(
        bool change
    )
    {
        // Arrange
        await using var context = new NotificationContext();
        var node = CreateNode();
        context.Attach(node);
        node.Name = "Bravo";
        var entry = context.Entry(node);
        var update = UpdateEntry(context, node);
        var plan = CapturePlan([entry]);

        if (change)
        {
            node.Name = "Charlie";
        }

        // Act
        var changed = PlanHasChanges(context, plan);

        // Assert
        Assert.Equal(change, changed);
        Assert.False(update.CanHaveOriginalValue(entry.Metadata.FindProperty(nameof(ProbeNode.Name))!));
        Assert.True(
            entry.Property(value => value.Name)
                .IsModified);
    }

    /// <summary>Mutable ordering snapshots own independent current and original values.</summary>
    /// <param name="original">Whether to mutate EF's original array instead of the current CLR array.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlanHasChanges_AuthorizedCandidate_ClonesMutableOrderingSnapshots(
        bool original
    )
    {
        // Arrange
        await using var context = new MutableOrderingPlanContext();
        var node = new MutablePlanNode
        {
            Id = 1,
            TreeId = Guid.NewGuid(),
            Left = 1,
            Right = 2,
            Rank = [1, 2],
        };

        var entry = context.Attach(node);
        entry.Property(value => value.Rank)
            .IsModified = true;

        var plan = CapturePlan([entry]);
        var values = original
            ? entry.Property(value => value.Rank)
                .OriginalValue
            : node.Rank;

        values[0] = 9;

        // Act
        var changed = PlanHasChanges(context, plan);

        // Assert
        Assert.True(changed);
        Assert.True(
            entry.Property(value => value.Rank)
                .IsModified);

        Assert.Equal(
            original
                ? (byte)1
                : (byte)9,
            node.Rank[0]);
    }
}

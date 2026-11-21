namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class InsertionReflectionContractTests
{
    /// <summary>Closed handler identity matches EF event subscriptions and never retains another entry.</summary>
    /// <param name="kind">The property or collection notification whose native handler is closed.</param>
    [Theory]
    [InlineData("changing")]
    [InlineData("changed")]
    [InlineData("collection")]
    public async Task CreateDelegate_Notifications_BindExactNativeHandlerIdentity(
        string kind
    )
    {
        // Arrange
        await using var context = new HierarchyChangeDetectionTests.SnapshotContext();
        var contract = NestedSetInsertionContract.Instance;
        var first = contract.Entry(context.Attach(HierarchyChangeDetectionTests.CreateNode()));
        var second = contract.Entry(context.Attach(HierarchyChangeDetectionTests.CreateNode(2)));
        var factory = NotificationFactory(contract, kind);

        // Act
        var handler = factory(first);
        var repeated = factory(first);
        var other = factory(second);

        // Assert
        Assert.Same(first, handler.Target);
        Assert.Same(second, other.Target);
        Assert.Equal(handler, repeated);
        Assert.NotEqual(handler, other);
        Assert.Single(handler.GetInvocationList());
        Assert.Equal(first.GetType(), handler.Method.DeclaringType);
        Assert.Equal(EntityState.Unchanged, first.EntityState);
        Assert.Equal(EntityState.Unchanged, second.EntityState);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Exact property delegates pause EF, retain application callbacks and resume native tracking.</summary>
    [Fact]
    public async Task IsModified_PropertyHandlers_PauseAndResumeWithoutDetachingOrRemovingApplicationListeners()
    {
        // Arrange
        await using var context = new NotificationBindingContext();
        var node = HierarchyChangeDetectionTests.CreateNode();
        node.Payload = "initial";
        var contract = NestedSetInsertionContract.Instance;
        var tracked = context.Attach(node);

        // WHY: Notification originals are lazy. Establish a real original before testing that pausing EF
        // preserves it; otherwise resumption correctly snapshots the current value when its slot is created.
        tracked.Property(value => value.Payload)
            .OriginalValue = "initial";

        var update = contract.Entry(tracked);
        var property = update.EntityType.FindProperty(nameof(HierarchyChangeDetectionTests.ProbeNode.Payload))!;
        var originalBefore = update.GetOriginalValue<string>(property);
        var modifiedBefore = update.IsModified(property);
        var changing = contract.PropertyChangingHandler(update);
        var changed = contract.PropertyChangedHandler(update);
        var applicationChanging = 0;
        var applicationChanged = 0;
        node.PropertyChanging += (_, _) => applicationChanging++;
        node.PropertyChanged += (_, _) => applicationChanged++;

        // Act
        node.PropertyChanging -= changing;
        node.PropertyChanged -= changed;
        node.Payload = "paused";
        var pausedModified = update.IsModified(property);
        var pausedState = update.EntityState;
        node.PropertyChanging += changing;
        node.PropertyChanged += changed;
        node.Payload = "resumed";

        // Assert
        Assert.Equal("initial", originalBefore);
        Assert.False(modifiedBefore);
        Assert.False(pausedModified);
        Assert.Equal(EntityState.Unchanged, pausedState);
        Assert.True(update.IsModified(property));
        Assert.Equal(EntityState.Modified, update.EntityState);
        Assert.Equal("initial", update.GetOriginalValue<string>(property));
        Assert.Equal("resumed", node.Payload);
        Assert.Equal(2, applicationChanging);
        Assert.Equal(2, applicationChanged);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Collection event pause leaves raw members untracked; restoring its handler resumes EF fixup.</summary>
    [Fact]
    public async Task CreateDelegate_CollectionHandler_PausesOnlyNativeNotificationFixup()
    {
        // Arrange
        await using var context = new NotificationBindingContext();
        var parent = HierarchyChangeDetectionTests.CreateNode();
        var child = HierarchyChangeDetectionTests.CreateNode(2);
        var contract = NestedSetInsertionContract.Instance;
        var update = contract.Entry(context.Attach(parent));
        var children = parent.Children!;
        var handler = contract.CollectionChangedHandler(update);
        var applicationEvents = 0;
        children.CollectionChanged += (_, _) => applicationEvents++;

        // Act
        children.CollectionChanged -= handler;
        children.Add(child);
        var pausedEntries = context
            .ChangeTracker
            .Entries()
            .Count();

        children.Remove(child);
        children.CollectionChanged += handler;
        children.Add(child);
        var childState = context.Entry(child)
            .State;

        // Assert
        Assert.Equal(1, pausedEntries);
        Assert.Equal(
            2,
            context
                .ChangeTracker
                .Entries()
                .Count());

        Assert.Equal(EntityState.Added, childState);
        Assert.Same(parent, child.Parent);
        Assert.Equal(parent.Id, child.ParentId);
        Assert.Same(children, parent.Children);
        Assert.Equal(3, applicationEvents);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }
}

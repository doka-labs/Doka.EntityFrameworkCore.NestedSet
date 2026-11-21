namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class InheritanceTests
{
    /// <summary>Rejects missing parents and mixed-subtype cycles without losing pending caller state.</summary>
    /// <param name="tpt">Whether structural state uses a TPT base fragment.</param>
    /// <param name="missing">Whether the requested parent is absent instead of a descendant.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SaveNestedSetChangesAsync_InvalidMixedSubtypeParent_RestoresPendingState(
        bool tpt,
        bool missing
    )
    {
        // Arrange
        await using var context = await CreateInheritanceContextAsync(
            tpt,
            cancellationToken: TestContext.Current.CancellationToken,
            ordered: false);

        var rootId = (tpt
                ? 10_001
                : 11_001)
            + (missing
                ? 100
                : 0);

        var hierarchy = context.NestedSet<InheritanceNode>();
        var treeId = Guid.NewGuid();
        await hierarchy.InsertRootAsync(Node(tpt, false, rootId, "Root"), treeId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(tpt, false, rootId + 1, "Branch"), rootId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(tpt, true, rootId + 2, "Descendant"), rootId + 1, CancellationToken.None);
        var tracked = await context
            .Set<InheritanceNode>()
            .Where(node => node.TreeId == treeId)
            .ToArrayAsync(CancellationToken.None);

        var before = await PersistedAsync(context, treeId, cancellationToken: TestContext.Current.CancellationToken);
        var changed = tracked.Single(node => node.Id == rootId + 1);
        var requestedParent = missing
            ? rootId + 99
            : rootId + 2;

        changed.ParentId = requestedParent;
        changed.Name = "Pending";

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveNestedSetChangesAsync(
            true,
            token => context.SaveChangesAsync(false, token),
            CancellationToken.None));

        // Assert
        Assert.Equal(
            missing
                ? NestedSetErrorCode.NodeNotFound
                : NestedSetErrorCode.CycleDetected,
            Assert.IsType<NestedSetException>(error)
                .Code);

        var persisted = await PersistedAsync(context, treeId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(before.Select(Snapshot), persisted.Select(Snapshot));
        Assert.Equal(requestedParent, changed.ParentId);
        Assert.Equal("Pending", changed.Name);
        Assert.Equal(
            EntityState.Modified,
            context.Entry(changed)
                .State);

        Assert.Equal(
            rootId,
            context
                .Entry(changed)
                .Property(node => node.ParentId)
                .OriginalValue);

        Assert.True(
            context
                .Entry(changed)
                .Property(node => node.ParentId)
                .IsModified);

        Assert.Equal(
            "Branch",
            context
                .Entry(changed)
                .Property(node => node.Name)
                .OriginalValue);

        Assert.True(
            context
                .Entry(changed)
                .Property(node => node.Name)
                .IsModified);
    }

    /// <summary>Rolls back a completed mixed-subtype payload save when the enclosing delegate fails.</summary>
    /// <param name="tpt">Whether structural state uses a TPT base fragment.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveNestedSetChangesAsync_MixedSubtypeSaveFailure_RollsBackPayloadAndTracker(
        bool tpt
    )
    {
        // Arrange
        await using var context = await CreateInheritanceContextAsync(
            tpt,
            cancellationToken: TestContext.Current.CancellationToken,
            ordered: false);

        var rootId = tpt
            ? 12_001
            : 13_001;

        var hierarchy = context.NestedSet<InheritanceNode>();
        var treeId = Guid.NewGuid();
        await hierarchy.InsertRootAsync(Node(tpt, false, rootId, "Root"), treeId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(tpt, false, rootId + 1, "Folder"), rootId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(tpt, true, rootId + 2, "Metric"), rootId, CancellationToken.None);
        var tracked = await context
            .Set<InheritanceNode>()
            .Where(node => node.TreeId == treeId)
            .ToArrayAsync(CancellationToken.None);

        var before = await PersistedAsync(context, treeId, cancellationToken: TestContext.Current.CancellationToken);
        var changed = tracked.Single(node => node.Id == rootId + 2);
        changed.ParentId = rootId + 1;
        changed.Name = "Pending";

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveNestedSetChangesAsync(
            true,
            async token =>
            {
                await context.SaveChangesAsync(false, token);

                throw new InjectedCommandException();
            },
            CancellationToken.None));

        // Assert
        Assert.IsType<InjectedCommandException>(error);
        var persisted = await PersistedAsync(context, treeId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(before.Select(Snapshot), persisted.Select(Snapshot));
        Assert.Equal(rootId + 1, changed.ParentId);
        Assert.Equal("Pending", changed.Name);
        Assert.Equal(
            EntityState.Modified,
            context.Entry(changed)
                .State);

        Assert.Equal(
            rootId,
            context
                .Entry(changed)
                .Property(node => node.ParentId)
                .OriginalValue);

        Assert.True(
            context
                .Entry(changed)
                .Property(node => node.ParentId)
                .IsModified);

        Assert.Equal(
            "Metric",
            context
                .Entry(changed)
                .Property(node => node.Name)
                .OriginalValue);

        Assert.True(
            context
                .Entry(changed)
                .Property(node => node.Name)
                .IsModified);

        Assert.Equal(
            before.Select(node => (node.Id, node.Left, node.Right, node.Depth, node.Position)),
            tracked
                .OrderBy(node => node.Left)
                .Select(node => (node.Id, node.Left, node.Right, node.Depth, node.Position)));
    }
}

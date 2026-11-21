namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

public abstract partial class TptInheritedCollationTestBase
{
    /// <summary>A concrete facade resolves inherited string identities through the configured base capture.</summary>
    /// <returns>A task that completes after verifying the parent, child, and inherited capture.</returns>
    [Fact]
    public async Task ParentOf_ConcreteLeafFacade_UsesConfiguredAncestorCaptures()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<TptInheritedCollationContext>(
            Engine,
            static options => new TptInheritedCollationContext(options),
            cancellationToken: TestContext.Current.CancellationToken);

        var suffix = Guid
            .NewGuid()
            .ToString("N");

        var scope = "FACADE-" + suffix;
        var treeId = Guid.NewGuid();
        var hierarchy = context
            .NestedSet<TptInheritedCollationNode>()
            .ForScope(scope);

        var root = new TptInheritedCollationLeaf
        {
            Id = "ROOT-" + suffix,
            Name = "Root",
            Payload = "Root leaf payload",
        };

        await hierarchy.InsertRootAsync(root, treeId, CancellationToken.None);
        var child = new TptInheritedCollationLeaf
        {
            Id = "CHILD-" + suffix,
            Name = "Child",
            Payload = "Child leaf payload",
        };

        await hierarchy.InsertChildAsync(child, root.Id, CancellationToken.None);
        context.ChangeTracker.Clear();
        var before = await TreeSnapshotsAsync(
            context,
            scope,
            treeId,
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var parent = await context
            .NestedSet<TptInheritedCollationLeaf>()
            .ForScope(scope)
            .ParentOf(child.Id)
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(before[0], Snapshot(parent));
        Assert.Equal(root.Id, parent.Id);
        Assert.Equal((1L, 4L, 0, 0L), (parent.Left, parent.Right, parent.Depth, parent.Position));
        Assert.Equal(
            before,
            await TreeSnapshotsAsync(
                context,
                scope,
                treeId,
                cancellationToken: TestContext.Current.CancellationToken));

        var leafType = context.Model.FindEntityType(typeof(TptInheritedCollationLeaf))!;
        Assert.Equal(
            "utf8mb4_bin",
            NestedSetCollations.Resolve(
                context,
                leafType.FindProperty(nameof(TptInheritedCollationNode.Scope))!,
                leafType));

        Assert.Equal(
            "utf8mb4_bin",
            NestedSetMapping<TptInheritedCollationLeaf, string, string>.For(context, leafType)
                .KeyCollation);

        Assert.True(
            (await hierarchy
                .InTree(treeId)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);

        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>The concrete facade cannot substitute default CI equality for its binary ancestor key.</summary>
    /// <returns>A task that completes after verifying exact input and persisted-state preservation.</returns>
    [Fact]
    public async Task InsertChildAsync_ConcreteLeafFacade_RejectsBinaryParentKeyAlias()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<TptInheritedCollationContext>(
            Engine,
            static options => new TptInheritedCollationContext(options),
            cancellationToken: TestContext.Current.CancellationToken);

        var suffix = Guid
            .NewGuid()
            .ToString("N");

        var scope = "BINARY-KEY-" + suffix;
        var treeId = Guid.NewGuid();
        var hierarchy = context
            .NestedSet<TptInheritedCollationNode>()
            .ForScope(scope);

        var root = new TptInheritedCollationLeaf
        {
            Id = "ROOT-" + suffix,
            Name = "Root",
            Payload = "Original root payload",
        };

        await hierarchy.InsertRootAsync(root, treeId, CancellationToken.None);
        context.ChangeTracker.Clear();
        var before = await TreeSnapshotsAsync(
            context,
            scope,
            treeId,
            cancellationToken: TestContext.Current.CancellationToken);

        var child = new TptInheritedCollationLeaf
        {
            Id = "REJECTED-" + suffix,
            Name = "Child",
            Payload = "Unwritten leaf payload",
        };

        var inputBefore = Snapshot(child);

        // Act
        var failure = await Record.ExceptionAsync(() => context
            .NestedSet<TptInheritedCollationLeaf>()
            .ForScope(scope)
            .InsertChildAsync(child, root.Id.ToLowerInvariant(), CancellationToken.None));

        // Assert
        Assert.Equal(
            NestedSetErrorCode.NodeNotFound,
            Assert.IsType<NestedSetException>(failure)
                .Code);

        Assert.Equal(inputBefore, Snapshot(child));
        Assert.Equal(
            EntityState.Detached,
            context.Entry(child)
                .State);

        Assert.Equal(
            before,
            await TreeSnapshotsAsync(
                context,
                scope,
                treeId,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.True(
            (await hierarchy
                .InTree(treeId)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);

        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Normal concrete leaf saves coordinate a Parent change using the configured ancestor capture.</summary>
    /// <returns>A task that completes after verifying geometry, payload, and accepted tracker state.</returns>
    [Fact]
    public async Task SaveChangesAsync_ConcreteLeafParentSave_UsesConfiguredAncestorCaptures()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<TptInheritedCollationContext>(
            Engine,
            static options => new TptInheritedCollationContext(options),
            cancellationToken: TestContext.Current.CancellationToken);

        var suffix = Guid
            .NewGuid()
            .ToString("N");

        var scope = "PARENT-SAVE-" + suffix;
        var treeId = Guid.NewGuid();
        var hierarchy = context
            .NestedSet<TptInheritedCollationNode>()
            .ForScope(scope);

        var root = new TptInheritedCollationLeaf
        {
            Id = "ROOT-" + suffix,
            Name = "Root",
            Payload = "Root leaf payload",
        };

        await hierarchy.InsertRootAsync(root, treeId, CancellationToken.None);
        var moved = new TptInheritedCollationLeaf
        {
            Id = "MOVED-" + suffix,
            Name = "Moved",
            Payload = "Original moved payload",
        };

        await hierarchy.InsertChildAsync(moved, root.Id, CancellationToken.None);
        var target = new TptInheritedCollationLeaf
        {
            Id = "TARGET-" + suffix,
            Name = "Target",
            Payload = "Target leaf payload",
        };

        await hierarchy.InsertChildAsync(target, root.Id, CancellationToken.None);
        context.ChangeTracker.Clear();
        var changed = await context
            .Set<TptInheritedCollationLeaf>()
            .SingleAsync(node => node.Id == moved.Id, CancellationToken.None);

        changed.ParentId = target.Id;
        changed.Name = "Reparented";
        changed.Payload = "Reparented leaf payload";

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        var snapshots = await TreeSnapshotsAsync(
            context,
            scope,
            treeId,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            new[]
            {
                (root.Id, scope, treeId, null, 1L, 6L, 0, 0L, root.Name, root.Payload),
                (target.Id, scope, treeId, (string?)root.Id, 2L, 5L, 1, 0L, target.Name, target.Payload),
                (moved.Id, scope, treeId, (string?)target.Id, 3L, 4L, 2, 0L, "Reparented",
                    "Reparented leaf payload"),
            },
            snapshots);

        Assert.Equal(snapshots[2], Snapshot(changed));
        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(changed)
                .State);

        Assert.Same(
            changed,
            Assert.Single(context.ChangeTracker.Entries())
                .Entity);

        Assert.True(
            (await hierarchy
                .InTree(treeId)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }
}

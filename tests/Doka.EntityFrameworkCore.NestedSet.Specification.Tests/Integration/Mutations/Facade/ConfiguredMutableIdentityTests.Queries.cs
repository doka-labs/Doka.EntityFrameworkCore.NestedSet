namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class ConfiguredMutableIdentityTests
{
    /// <summary>Deferred anchor execution retains its configured Scope and NodeKey snapshots.</summary>
    /// <returns>A task that completes after verifying the original visible sibling group.</returns>
    [Fact]
    public async Task DeferredAnchorQueryOwnsConfiguredScopeAndNodeKey()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync(Engine);
        await SeedQueryRowsAsync(context);
        var scope = new MutableIdentity("scope");
        var nodeKey = new MutableIdentity("root");
        var query = context
            .NestedSet<ConfiguredIdentityNode>()
            .ForScope(scope)
            .ChildrenOf(nodeKey);

        scope.Value = "foreign-scope";
        nodeKey.Value = "foreign-root";

        // Act
        var rows = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        var child = Assert.Single(rows);
        Assert.Equal(
            ("child", "scope", "source", "root", 2L, 3L, 1, 0L),
            (child.Id.Value, child.Scope.Value, child.TreeId.Value, child.ParentId!.Value, child.Left, child.Right,
                child.Depth, child.Position));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Deferred tree queries retain the same owned TreeId used by the tree facade.</summary>
    /// <returns>A task that completes after verifying the original Scope and complete tree.</returns>
    [Fact]
    public async Task DeferredTreeQueryOwnsConfiguredScopeAndTreeId()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync(Engine);
        await SeedQueryRowsAsync(context);
        var scope = new MutableIdentity("scope");
        var treeId = new MutableIdentity("source");
        var tree = context
            .NestedSet<ConfiguredIdentityNode>()
            .ForScope(scope)
            .InTree(treeId);

        scope.Value = "foreign-scope";
        treeId.Value = "foreign-tree";

        // Act
        var rows = await tree.Nodes.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Collection(
            rows,
            root => Assert.Equal(("root", 1L, 4L, 0), (root.Id.Value, root.Left, root.Right, root.Depth)),
            child => Assert.Equal(("child", 2L, 3L, 1), (child.Id.Value, child.Left, child.Right, child.Depth)));
        Assert.All(rows, row => Assert.Equal(("scope", "source"), (row.Scope.Value, row.TreeId.Value)));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Tree validation cannot be redirected by edits to the caller's already bound identities.</summary>
    /// <returns>A task that completes after verifying the original two-node tree.</returns>
    [Fact]
    public async Task TreeValidationOwnsConfiguredScopeAndTreeId()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync(Engine);
        await SeedQueryRowsAsync(context);
        var scope = new MutableIdentity("scope");
        var treeId = new MutableIdentity("source");
        var tree = context
            .NestedSet<ConfiguredIdentityNode>()
            .ForScope(scope)
            .InTree(treeId);

        var before = await RowsAsync(context);
        var registryBefore = await RegistriesAsync(context);
        scope.Value = "missing-scope";
        treeId.Value = "missing-tree";

        // Act
        var report = await tree.ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        // Assert
        Assert.True(report.IsValid);
        Assert.Equal(2, report.NodeCount);
        Assert.Equal(before, await RowsAsync(context));
        Assert.Equal(registryBefore, await RegistriesAsync(context));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Rebuild planning shares the owned tree identity and preserves every persisted row.</summary>
    /// <returns>A task that completes after verifying the original tree needs no repair.</returns>
    [Fact]
    public async Task RebuildPlanOwnsConfiguredScopeAndTreeId()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync(Engine);
        await SeedQueryRowsAsync(context);
        var scope = new MutableIdentity("scope");
        var treeId = new MutableIdentity("source");
        var tree = context
            .NestedSet<ConfiguredIdentityNode>()
            .ForScope(scope)
            .InTree(treeId);

        var before = await RowsAsync(context);
        var registryBefore = await RegistriesAsync(context);
        scope.Value = "missing-scope";
        treeId.Value = "missing-tree";

        // Act
        var plan = await tree.PlanRebuildAsync(CancellationToken.None);

        // Assert
        Assert.True(plan.CanRebuild);
        Assert.Equal(2, plan.NodeCount);
        Assert.Equal(0, plan.ChangedNodeCount);
        Assert.Equal(before, await RowsAsync(context));
        Assert.Equal(registryBefore, await RegistriesAsync(context));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Creates distinguishable trees in the original Scope and a foreign application partition.</summary>
    private static async Task SeedQueryRowsAsync(
        DbContext context
    )
    {
        var hierarchy = context
            .NestedSet<ConfiguredIdentityNode>()
            .ForScope(new MutableIdentity("scope"));

        await hierarchy.InsertRootAsync(
            new ConfiguredIdentityNode { Id = new MutableIdentity("root") },
            new MutableIdentity("source"),
            CancellationToken.None);

        await hierarchy.InsertChildAsync(
            new ConfiguredIdentityNode { Id = new MutableIdentity("child") },
            new MutableIdentity("root"),
            CancellationToken.None);

        await hierarchy.InsertRootAsync(
            new ConfiguredIdentityNode { Id = new MutableIdentity("other-root") },
            new MutableIdentity("other-tree"),
            CancellationToken.None);

        await hierarchy.InsertChildAsync(
            new ConfiguredIdentityNode { Id = new MutableIdentity("other-child") },
            new MutableIdentity("other-root"),
            CancellationToken.None);

        var foreign = context
            .NestedSet<ConfiguredIdentityNode>()
            .ForScope(new MutableIdentity("foreign-scope"));

        await foreign.InsertRootAsync(
            new ConfiguredIdentityNode { Id = new MutableIdentity("foreign-root") },
            new MutableIdentity("foreign-tree"),
            CancellationToken.None);

        await foreign.InsertChildAsync(
            new ConfiguredIdentityNode { Id = new MutableIdentity("foreign-child") },
            new MutableIdentity("foreign-root"),
            CancellationToken.None);
    }
}

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class RelationalTests
{
    /// <summary>Guid keys and nullable Guid parent references support direct-child queries.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task GuidChildrenResolveNullableParent()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<GuidNode>()
            .ForScope(1);
        var parent = Guid.NewGuid();
        var child = Guid.NewGuid();
        await tree.InsertRootAsync(new GuidNode { Id = parent }, Guid.Empty, CancellationToken.None);
        await tree.InsertChildAsync(new GuidNode { Id = child }, parent, cancellationToken: CancellationToken.None);

        // Act
        var children = await tree
            .ChildrenOf(parent)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(child, Assert.Single(children).Id);
        Assert.True(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }

    /// <summary>Detaching a Guid-keyed child creates an independent tree and clears its nullable parent.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task GuidDetachmentCreatesAnIndependentRoot()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<GuidNode>()
            .ForScope(1);

        var parent = Guid.NewGuid();
        var child = Guid.NewGuid();
        await tree.InsertRootAsync(new GuidNode { Id = parent }, Guid.Empty, CancellationToken.None);
        await tree.InsertChildAsync(new GuidNode { Id = child }, parent, cancellationToken: CancellationToken.None);

        // Act
        await tree.DetachAsTreeAsync(child, Guid.NewGuid(), CancellationToken.None);

        // Assert
        var moved = await context
            .Set<GuidNode>()
            .AsNoTracking()
            .Where(node => node.Tree == 1)
            .SingleAsync(x => x.Id == child, CancellationToken.None);

        Assert.Null(moved.ParentId);
        Assert.Equal(0, moved.Depth);
        Assert.Equal((1L, 2L, 0L), (moved.Left, moved.Right, moved.Position));
        Assert.NotEqual(Guid.Empty, moved.TreeId);
        Assert.True(
            (await tree
                .InTree(moved.TreeId)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
        Assert.True(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }

    /// <summary>
    ///     Moving a Guid-keyed root into another tree sets the nullable parent through the typed updater.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task GuidCrossTreeMoveSetsTypedParent()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<GuidNode>()
            .ForScope(1);

        var parent = Guid.NewGuid();
        var child = Guid.NewGuid();
        await tree.InsertRootAsync(new GuidNode { Id = parent }, Guid.Empty, CancellationToken.None);
        await tree.InsertChildAsync(new GuidNode { Id = child }, parent, cancellationToken: CancellationToken.None);
        await tree.DetachAsTreeAsync(child, Guid.NewGuid(), CancellationToken.None);

        // Act
        await tree.MoveToAsync(child, parent, CancellationToken.None);

        // Assert
        Assert.Equal(
            parent,
            (await tree
                .ParentOf(child)
                .SingleAsync(CancellationToken.None)).Id);
        Assert.True(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }

    /// <summary>String anchor lookup stores the canonical database key under provider collation.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task StringInsertStoresCanonicalParentKey()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TextNode>()
            .ForScope("tenant");

        await tree.InsertRootAsync(new TextNode { Id = "Parent" }, Guid.Empty, CancellationToken.None);
        var lookup = ParentLookup(Engine);

        // Act
        await tree.InsertChildAsync(new TextNode { Id = "Child" }, lookup, cancellationToken: CancellationToken.None);

        // Assert
        Assert.Equal(
            "Parent",
            (await context
                .Set<TextNode>()
                .AsNoTracking()
                .Where(node => node.Tree == "tenant")
                .SingleAsync(x => x.Id == "Child", CancellationToken.None)).ParentId);
        Assert.True(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }

    /// <summary>String parent aliases use primary-key collation even when parent-column collation differs.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task StringRebuildResolvesStoredParentAlias()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TextNode>()
            .ForScope("tenant");

        await tree.InsertRootAsync(new TextNode { Id = "Parent" }, Guid.Empty, CancellationToken.None);
        var lookup = ParentLookup(Engine);
        await tree.InsertChildAsync(new TextNode { Id = "Child" }, lookup, cancellationToken: CancellationToken.None);
        await context
            .Set<TextNode>()
            .AsNoTracking()
            .Where(node => node.Tree == "tenant")
            .Where(x => x.Id == "Child")
            .ExecuteUpdateAsync(x => x.SetProperty(n => n.ParentId, lookup), CancellationToken.None);

        // Act
        await tree
            .InTree(Guid.Empty)
            .RebuildAsync(CancellationToken.None);

        // Assert
        Assert.True(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
        Assert.Equal(
            "Child",
            (await tree
                .ChildrenOf(lookup)
                .SingleAsync(CancellationToken.None)).Id);
    }

    /// <summary>String parent aliases use primary-key collation even when parent-column collation differs.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task StringChildrenUsesKeyCollationForStoredParentAlias()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TextNode>()
            .ForScope("tenant");

        await tree.InsertRootAsync(new TextNode { Id = "Parent" }, Guid.Empty, CancellationToken.None);
        var lookup = ParentLookup(Engine);
        await tree.InsertChildAsync(new TextNode { Id = "Child" }, lookup, cancellationToken: CancellationToken.None);
        await context
            .Set<TextNode>()
            .AsNoTracking()
            .Where(node => node.Tree == "tenant")
            .Where(x => x.Id == "Child")
            .ExecuteUpdateAsync(x => x.SetProperty(n => n.ParentId, lookup), CancellationToken.None);

        // Act
        var children = await tree
            .ChildrenOf(lookup)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal("Child", Assert.Single(children).Id);
    }

    /// <summary>Moving a string-keyed subtree to the forest clears its parent.</summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task StringDetachmentCreatesAnIndependentRoot()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TextNode>()
            .ForScope("tenant");

        await tree.InsertRootAsync(new TextNode { Id = "Parent" }, Guid.Empty, CancellationToken.None);
        var lookup = ParentLookup(Engine);
        await tree.InsertChildAsync(new TextNode { Id = "Child" }, lookup, cancellationToken: CancellationToken.None);
        await context
            .Set<TextNode>()
            .AsNoTracking()
            .Where(node => node.Tree == "tenant")
            .Where(x => x.Id == "Child")
            .ExecuteUpdateAsync(x => x.SetProperty(n => n.ParentId, lookup), CancellationToken.None);

        // Act
        await tree.DetachAsTreeAsync("Child", Guid.NewGuid(), CancellationToken.None);

        // Assert
        var moved = await context
            .Set<TextNode>()
            .AsNoTracking()
            .Where(node => node.Tree == "tenant")
            .SingleAsync(x => x.Id == "Child", CancellationToken.None);

        Assert.Null(moved.ParentId);
        Assert.Equal(0, moved.Depth);
        Assert.Equal((1L, 2L, 0L), (moved.Left, moved.Right, moved.Position));
        Assert.NotEqual(Guid.Empty, moved.TreeId);
        Assert.True(
            (await tree
                .InTree(moved.TreeId)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
        Assert.True(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }

    /// <summary>
    ///     Moving a string-keyed root into another tree resolves the canonical parent key under database collation.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task StringCrossTreeMoveStoresCanonicalParent()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TextNode>()
            .ForScope("tenant");

        await tree.InsertRootAsync(new TextNode { Id = "Parent" }, Guid.Empty, CancellationToken.None);
        var lookup = ParentLookup(Engine);
        await tree.InsertRootAsync(new TextNode { Id = "Child" }, Guid.NewGuid(), CancellationToken.None);

        // Act
        await tree.MoveToAsync("Child", lookup, CancellationToken.None);

        // Assert
        Assert.Equal(
            "Parent",
            (await tree
                .ParentOf("Child")
                .SingleAsync(CancellationToken.None)).Id);
        Assert.True(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }

    /// <summary>Returns an anchor spelling that exercises the key collation configured by each fixture.</summary>
    private static string ParentLookup(
        string engine
    ) =>
        // WHY: PostgreSQL keys are case-sensitive; the other fixture collations deliberately accept aliases.
        engine == "PostgreSql" ? "Parent" : "PARENT";
}

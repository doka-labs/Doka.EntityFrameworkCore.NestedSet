namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class RelationalTests
{
    /// <summary>Deletes or promotes a Guid-keyed branch using its nullable parent mapping.</summary>
    /// <param name="subtree">Whether descendants are removed instead of promoted.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GuidDeletionPreservesTypedParentSemantics(
        bool subtree
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<GuidNode>()
            .ForScope(1);

        var root = Guid.NewGuid();
        var branch = Guid.NewGuid();
        var leaf = Guid.NewGuid();
        await tree.InsertRootAsync(new GuidNode { Id = root }, Guid.Empty, CancellationToken.None);
        await tree.InsertChildAsync(new GuidNode { Id = branch }, root, CancellationToken.None);
        await tree.InsertChildAsync(new GuidNode { Id = leaf }, branch, CancellationToken.None);
        var other = context
            .NestedSet<GuidNode>()
            .ForScope(2);

        var otherRoot = new GuidNode { Id = Guid.NewGuid() };
        await other.InsertRootAsync(otherRoot, Guid.Empty, CancellationToken.None);
        await other.InsertChildAsync(new GuidNode { Id = Guid.NewGuid() }, otherRoot.Id, CancellationToken.None);
        var beforeOther = (await context
                .Set<GuidNode>()
                .AsNoTracking()
                .Where(node => node.Tree == 2)
                .OrderBy(node => node.Left)
                .ToArrayAsync(CancellationToken.None))
            .Select(node => (node.Id, node.Tree, node.TreeId, node.ParentId, node.Left, node.Right, node.Depth,
                node.Position))
            .ToArray();

        // Act
        if (subtree)
        {
            await tree.DeleteSubtreeAsync(branch, CancellationToken.None);
        }
        else
        {
            await tree.DeleteAsync(branch, CancellationToken.None);
        }

        // Assert
        var nodes = await context
            .Set<GuidNode>()
            .AsNoTracking()
            .Where(node => node.Tree == 1)
            .OrderBy(node => node.Left)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(subtree ? 1 : 2, nodes.Length);
        Assert.Equal(root, nodes[0].Id);
        Assert.DoesNotContain(nodes, node => node.Id == branch);

        if (!subtree)
        {
            Assert.Equal(leaf, nodes[1].Id);
            Assert.Equal(root, nodes[1].ParentId);
            Assert.Equal(1, nodes[1].Depth);
        }

        Assert.True(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);

        var afterOther = (await context
                .Set<GuidNode>()
                .AsNoTracking()
                .Where(node => node.Tree == 2)
                .OrderBy(node => node.Left)
                .ToArrayAsync(CancellationToken.None))
            .Select(node => (node.Id, node.Tree, node.TreeId, node.ParentId, node.Left, node.Right, node.Depth,
                node.Position))
            .ToArray();

        Assert.Equal(beforeOther, afterOther);
    }

    /// <summary>Deletes a string-keyed branch through the database's key equality contract.</summary>
    /// <param name="subtree">Whether descendants are removed instead of promoted.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StringDeletionResolvesTheCanonicalParent(
        bool subtree
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TextNode>()
            .ForScope("tenant");

        await tree.InsertRootAsync(new TextNode { Id = "Root" }, Guid.Empty, CancellationToken.None);
        await tree.InsertChildAsync(new TextNode { Id = "Parent" }, "Root", CancellationToken.None);
        await tree.InsertChildAsync(new TextNode { Id = "Leaf" }, "Parent", CancellationToken.None);
        var other = context
            .NestedSet<TextNode>()
            .ForScope("other");

        await other.InsertRootAsync(new TextNode { Id = "OtherRoot" }, Guid.Empty, CancellationToken.None);
        await other.InsertChildAsync(new TextNode { Id = "OtherLeaf" }, "OtherRoot", CancellationToken.None);
        var beforeOther = (await context
                .Set<TextNode>()
                .AsNoTracking()
                .Where(node => node.Tree == "other")
                .OrderBy(node => node.Left)
                .ToArrayAsync(CancellationToken.None))
            .Select(node => (node.Id, node.Tree, node.TreeId, node.ParentId, node.Left, node.Right, node.Depth,
                node.Position))
            .ToArray();

        var lookup = ParentLookup(Engine);

        // Act
        if (subtree)
        {
            await tree.DeleteSubtreeAsync(lookup, CancellationToken.None);
        }
        else
        {
            await tree.DeleteAsync(lookup, CancellationToken.None);
        }

        // Assert
        var nodes = await context
            .Set<TextNode>()
            .AsNoTracking()
            .Where(node => node.Tree == "tenant")
            .OrderBy(node => node.Left)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(subtree ? 1 : 2, nodes.Length);
        Assert.Equal("Root", nodes[0].Id);
        Assert.DoesNotContain(nodes, node => node.Id == "Parent");

        if (!subtree)
        {
            Assert.Equal("Leaf", nodes[1].Id);
            Assert.Equal("Root", nodes[1].ParentId);
            Assert.Equal(1, nodes[1].Depth);
        }

        Assert.True(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);

        var afterOther = (await context
                .Set<TextNode>()
                .AsNoTracking()
                .Where(node => node.Tree == "other")
                .OrderBy(node => node.Left)
                .ToArrayAsync(CancellationToken.None))
            .Select(node => (node.Id, node.Tree, node.TreeId, node.ParentId, node.Left, node.Right, node.Depth,
                node.Position))
            .ToArray();

        Assert.Equal(beforeOther, afterOther);
    }
}

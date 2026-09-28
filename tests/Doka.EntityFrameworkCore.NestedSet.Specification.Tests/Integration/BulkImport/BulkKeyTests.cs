namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies batched adjacency and refresh predicates for nonnumeric application-assigned identities.</summary>
public abstract class BulkKeyTests : ProviderTest
{
    private readonly RelationalFixture _fixture;

    /// <summary>Uses fixture-owned databases with existing Guid, string and binary hierarchy mappings.</summary>
    protected BulkKeyTests(
        IProviderFixture<RelationalFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Guid parents retain their mapped database representation through batched import updates.</summary>
    [Fact]
    public async Task GuidAdjacencySurvivesImport()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<GuidNode>()
            .ForScope(1);

        var root = new GuidNode { Id = Guid.NewGuid() };
        var child = new GuidNode { Id = Guid.NewGuid() };
        var branch = new NestedSetBranch<GuidNode>(root, [new NestedSetBranch<GuidNode>(child)]);

        // Act
        await tree.InsertForestAsync(
            [new NestedSetTreeImport<GuidNode, Guid>(Guid.Empty, branch),],
            CancellationToken.None);

        // Assert
        Assert.Equal(root.Id, child.ParentId);
        Assert.Equal(
            new[] { root.Id, child.Id },
            await tree
                .TreeContaining(child.Id)
                .Select(node => node.Id)
                .ToArrayAsync(CancellationToken.None));
        Assert.Empty(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);
    }

    /// <summary>String values are parameters and retain scope isolation while assigning parent links.</summary>
    [Fact]
    public async Task StringAdjacencySurvivesImport()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TextNode>()
            .ForScope("scope's name");

        var root = new TextNode { Id = "parent's key" };
        var child = new TextNode { Id = "child's key" };
        var branch = new NestedSetBranch<TextNode>(root, [new NestedSetBranch<TextNode>(child)]);

        // Act
        await tree.InsertForestAsync(
            [new NestedSetTreeImport<TextNode, Guid>(Guid.Empty, branch),],
            CancellationToken.None);

        // Assert
        Assert.Equal(root.Id, child.ParentId);
        Assert.Equal("scope's name", child.Tree);
        Assert.Empty(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);
    }

    /// <summary>Binary keys use content equality when native result arrays differ from caller-owned arrays.</summary>
    [Fact]
    public async Task BinaryAdjacencySurvivesImport()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<BinaryNode>()
            .ForScope(1);

        var root = new BinaryNode { Id = [1, 2, 3] };
        var child = new BinaryNode { Id = [4, 5, 6] };
        var branch = new NestedSetBranch<BinaryNode>(root, [new NestedSetBranch<BinaryNode>(child)]);

        // Act
        await tree.InsertForestAsync(
            [new NestedSetTreeImport<BinaryNode, Guid>(Guid.Empty, branch),],
            CancellationToken.None);

        // Assert
        Assert.Equal(root.Id, child.ParentId);
        Assert.Equal((2, 3, 1), (child.Left, child.Right, child.Depth));
        Assert.Empty(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);
    }
}

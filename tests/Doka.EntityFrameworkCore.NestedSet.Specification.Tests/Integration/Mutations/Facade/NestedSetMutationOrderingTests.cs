namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies the target facade against configured and manual sibling-order policies.</summary>
public abstract class NestedSetMutationOrderingTests : ProviderTest
{
    private readonly OrderingFixture _fixture;

    /// <summary>Creates a facade ordering test over the established provider fixture.</summary>
    protected NestedSetMutationOrderingTests(
        IProviderFixture<OrderingFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Configured ordering determines sibling positions and complete-tree preorder.</summary>
    [Fact]
    public async Task AutomaticInsertUsesConfiguredOrder()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var hierarchy = context
            .NestedSet<OrderingNode>()
            .ForScope(1);

        var treeId = Guid.NewGuid();
        await hierarchy.InsertRootAsync(Node(1, "Root"), treeId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(2, "Zulu"), 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(3, "Alpha"), 1, CancellationToken.None);

        // Act
        await hierarchy.InsertChildAsync(Node(4, "Middle"), 1, CancellationToken.None);
        var nodes = await hierarchy
            .InTree(treeId)
            .Nodes
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([1, 3, 4, 2], nodes.Select(node => node.Id));
        Assert.Equal([0L, 1L, 2L], nodes.Skip(1).Select(node => node.Position));
    }

    /// <summary>Configured-only ordering rejects manual placement without changing persisted structure.</summary>
    [Fact]
    public async Task StrictOrderRejectsManualPlacement()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var hierarchy = context
            .NestedSet<OrderingNode>()
            .ForScope(1);

        var treeId = Guid.NewGuid();
        await hierarchy.InsertRootAsync(Node(1, "Root"), treeId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(2, "Alpha"), 1, CancellationToken.None);

        // Act
        var error = await Assert.ThrowsAsync<NestedSetException>(() => hierarchy.InsertBeforeAsync(
            Node(3, "Zulu"),
            2,
            CancellationToken.None));

        var nodeIds = await hierarchy
            .InTree(treeId)
            .Nodes
            .Select(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(NestedSetErrorCode.ManualPlacementNotAllowed, error.Code);
        Assert.Equal([1, 2], nodeIds);
    }

    /// <summary>Flexible ordering honors a caller-selected position for one explicit insertion.</summary>
    [Fact]
    public async Task FlexibleOrderAllowsManualPlacement()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine, "Flexible");
        var hierarchy = context
            .NestedSet<OrderingNode>()
            .ForScope(1);

        var treeId = Guid.NewGuid();
        await hierarchy.InsertRootAsync(Node(1, "Root"), treeId, CancellationToken.None);
        await hierarchy.InsertChildAsync(Node(2, "Alpha"), 1, CancellationToken.None);

        // Act
        await hierarchy.InsertBeforeAsync(Node(3, "Zulu"), 2, CancellationToken.None);
        var children = await hierarchy
            .ChildrenOf(1)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([3, 2], children.Select(node => node.Id));
        Assert.Equal([0L, 1L], children.Select(node => node.Position));
    }

    /// <summary>Creates one detached node whose structural values are assigned by the facade.</summary>
    private static OrderingNode Node(
        int id,
        string name
    ) => new() { Id = id, Name = name };
}

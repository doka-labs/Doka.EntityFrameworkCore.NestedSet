namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies the supported concrete and rejected polymorphic TPC boundaries.</summary>
[Collection("Model compatibility")]
public abstract class TpcTests : ProviderTest
{
    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Shares one provider database with the other mapping-compatibility tests.</summary>
    protected TpcTests(
        IProviderFixture<ModelCompatibilityDatabase> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Uses one concrete TPC table as an independent hierarchy entity set.</summary>
    [Fact]
    public async Task ConcreteTpcEntitySupportsIndependentTreeAsync()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<ConcreteTpcContext>(
            Engine,
            static options => new ConcreteTpcContext(options));
        var hierarchy = context.NestedSet<TpcFolderNode>();
        var treeId = Guid.NewGuid();

        // Act
        await hierarchy.InsertRootAsync(
            new TpcFolderNode
            {
                Id = 1,
                Name = "Root",
            },
            treeId,
            CancellationToken.None);

        await hierarchy.InsertChildAsync(
            new TpcFolderNode
            {
                Id = 2,
                Name = "Child",
            },
            1,
            CancellationToken.None);

        var nodes = await hierarchy
            .InTree(treeId)
            .Nodes
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Collection(
            nodes,
            node => Assert.Equal((1, "Root", 0), (node.Id, node.Name, node.Depth)),
            node => Assert.Equal((2, "Child", 1), (node.Id, node.Name, node.Depth)));
    }

    /// <summary>Rejects a base hierarchy whose polymorphic TPC query spans multiple tables.</summary>
    [DatabaseIndependent]
    [Fact]
    public void PolymorphicTpcHierarchyIsRejected()
    {
        // Arrange
        var options = ModelCompatibilityDatabase.Options<PolymorphicTpcContext>(Engine);
        using var context = new PolymorphicTpcContext(options);

        // Act
        var exception = Record.Exception(() => context.NestedSet<TpcNode>());

        // Assert
        var invalidOperation = Assert.IsAssignableFrom<InvalidOperationException>(exception);
        Assert.Contains("polymorphic TPC", invalidOperation.Message, StringComparison.Ordinal);
    }
}

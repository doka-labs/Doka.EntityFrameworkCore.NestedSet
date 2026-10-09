namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies named shared-type hierarchy queries and mutations against a relational database.</summary>
[Collection("Model compatibility")]
public abstract class SharedTypeTests : ProviderTest
{
    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Shares one provider database with the other mapping-compatibility tests.</summary>
    protected SharedTypeTests(IProviderFixture<ModelCompatibilityDatabase> fixture) : base(fixture) => _fixture = fixture.Value;

    /// <summary>Uses the explicit entity-type name for every insert, structural write, and query.</summary>
    [Fact]
    public async Task NamedSharedTypeSupportsInsertAndQueryAsync()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<SharedTypeContext>(
            Engine, static options => new SharedTypeContext(options));
        var treeId = Guid.NewGuid();
        var root = Node(1, "Root");
        var child = Node(2, "Child");
        var folders = context.NestedSet<Dictionary<string, object>>(SharedTypeContext.FolderEntity);

        // Act
        await folders.InsertRootAsync(root, treeId, CancellationToken.None);
        await folders.InsertChildAsync(child, 1, CancellationToken.None);
        var rows = await folders
            .InTree(treeId)
            .Nodes
            .Select(node => new
            {
                Id = EF.Property<int>(node, SharedTypeContext.Id),
                Left = EF.Property<long>(node, SharedTypeContext.Left),
                Right = EF.Property<long>(node, SharedTypeContext.Right),
                Depth = EF.Property<int>(node, SharedTypeContext.Depth),
            })
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Collection(
            rows,
            node =>
            {
                Assert.Equal(1, node.Id);
                Assert.Equal(1, node.Left);
                Assert.Equal(4, node.Right);
                Assert.Equal(0, node.Depth);
            },
            node =>
            {
                Assert.Equal(2, node.Id);
                Assert.Equal(2, node.Left);
                Assert.Equal(3, node.Right);
                Assert.Equal(1, node.Depth);
            });
    }

    /// <summary>Rejects ambiguous CLR-only access when the model contains named shared entity types.</summary>
    [DatabaseIndependent]
    [Fact]
    public void SharedTypeWithoutEntityNameIsRejected()
    {
        // Arrange
        var options = ModelCompatibilityDatabase.Options<SharedTypeContext>(Engine);
        using var context = new SharedTypeContext(options);

        // Act
        var exception = Record.Exception(context.NestedSet<Dictionary<string, object>>);

        // Assert
        var invalidOperation = Assert.IsAssignableFrom<InvalidOperationException>(exception);
        Assert.Contains("shared type", invalidOperation.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Creates one detached property-bag node without caller-assigned structural values.</summary>
    private static Dictionary<string, object> Node(
        int id,
        string name
    ) => new()
    {
        [SharedTypeContext.Id] = id,
        [SharedTypeContext.Name] = name,
    };
}

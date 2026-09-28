namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Verifies that nested-set insertion preserves owned and complex application payload.</summary>
public sealed class PayloadTests : ProviderTest, IClassFixture<ProviderFixture<ProviderResources, SqliteEngine>>
{
    /// <summary>Uses immutable SQLite ownership while each test owns its local resources.</summary>
    /// <param name="fixture">The provider fixture identifying this suite's SQLite engine.</param>
    public PayloadTests(
        ProviderFixture<ProviderResources, SqliteEngine> fixture
    ) : base(fixture) { }

    /// <summary>Persists an owned aggregate together with the detached root node.</summary>
    [Fact]
    public async Task OwnedPayloadIsInsertedWithNodeAsync()
    {
        // Arrange
        var options = Options();
        await using var context = new PayloadContext(options);
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var node = new OwnedPayloadNode
        {
            Id = 1,
            Details = new OwnedNodeDetails { Label = "Owned payload" },
        };

        // Act
        await context
            .NestedSet<OwnedPayloadNode>()
            .InsertRootAsync(node, Guid.NewGuid(), CancellationToken.None);

        var persisted = await context
            .Set<OwnedPayloadNode>()
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal("Owned payload", persisted.Details.Label);
        Assert.Equal((1L, 2L, 0), (persisted.Left, persisted.Right, persisted.Depth));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Persists inline complex payload without treating it as a relationship graph.</summary>
    [Fact]
    public async Task ComplexPayloadIsInsertedWithNodeAsync()
    {
        // Arrange
        var options = Options();
        await using var context = new PayloadContext(options);
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var node = new ComplexPayloadNode
        {
            Id = 1,
            Details = new ComplexNodeDetails { Label = "Complex payload" },
        };

        // Act
        await context
            .NestedSet<ComplexPayloadNode>()
            .InsertRootAsync(node, Guid.NewGuid(), CancellationToken.None);

        var persisted = await context
            .Set<ComplexPayloadNode>()
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal("Complex payload", persisted.Details.Label);
        Assert.Equal((1L, 2L, 0), (persisted.Left, persisted.Right, persisted.Depth));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Persists owned payload for every node in a bulk subtree import.</summary>
    [Fact]
    public async Task BulkImportIncludesOwnedPayloadAsync()
    {
        // Arrange
        var options = Options();
        await using var context = new PayloadContext(options);
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var hierarchy = context.NestedSet<OwnedPayloadNode>();
        var treeId = Guid.NewGuid();
        await hierarchy.InsertRootAsync(
            new OwnedPayloadNode
            {
                Id = 1,
                Details = new OwnedNodeDetails { Label = "Root payload" },
            },
            treeId,
            CancellationToken.None);

        var subtree = new NestedSetBranch<OwnedPayloadNode>(
            new OwnedPayloadNode
            {
                Id = 2,
                Details = new OwnedNodeDetails { Label = "Branch payload" },
            },
            [
                new NestedSetBranch<OwnedPayloadNode>(
                    new OwnedPayloadNode
                    {
                        Id = 3,
                        Details = new OwnedNodeDetails { Label = "Leaf payload" },
                    }),
            ]);

        // Act
        await hierarchy.InsertSubtreeAsync(subtree, 1, CancellationToken.None);
        var nodes = await hierarchy
            .InTree(treeId)
            .Nodes
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Collection(
            nodes,
            node => Assert.Equal("Root payload", node.Details.Label),
            node => Assert.Equal("Branch payload", node.Details.Label),
            node => Assert.Equal("Leaf payload", node.Details.Label));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Builds one options instance with automatic nested-set integration.</summary>
    private static DbContextOptions<PayloadContext> Options() => new DbContextOptionsBuilder<PayloadContext>()
        .ConfigureTestWarnings()
        .UseSqlite("Data Source=:memory:")
        .UseNestedSets()
        .Options;
}

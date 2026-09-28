namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Verifies real SQL Server generated concurrency values after structural and coordinated writes.</summary>
public sealed class RowVersionTests : ProviderTest, IClassFixture<ProviderFixture<RowVersionFixture, SqlServerEngine>>
{
    private readonly RowVersionFixture _fixture;

    /// <summary>Creates cases using an isolated rowversion-enabled hierarchy.</summary>
    /// <param name="fixture">The fixture bound to this provider suite.</param>
    public RowVersionTests(
        ProviderFixture<RowVersionFixture, SqlServerEngine> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Verifies detached insertion returns a fresh token after automatic subtree relocation.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SortedInsertionReturnsATokenThatCanBeAttachedAndSaved(
        bool readCommittedSnapshot
    )
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(readCommittedSnapshot);
        var tree = context
            .NestedSet<SqlServerVersionNode>()
            .ForScope(1);

        var root = new SqlServerVersionNode { Name = "Root" };
        await tree.InsertRootAsync(root, Guid.Empty, CancellationToken.None);
        await tree.InsertChildAsync(new SqlServerVersionNode { Name = "Zulu" }, root.Id, CancellationToken.None);

        var inserted = new SqlServerVersionNode { Name = "Alpha" };
        await tree.InsertChildAsync(inserted, root.Id, CancellationToken.None);
        var initialToken = inserted.Version.ToArray();
        context.Attach(inserted);
        inserted.Name = "Omega";

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.NotEmpty(initialToken);
        Assert.NotEqual(initialToken, inserted.Version);
        var persisted = await context
            .Set<SqlServerVersionNode>()
            .AsNoTracking()
            .SingleAsync(node => node.Id == inserted.Id, CancellationToken.None);

        Assert.Equal(persisted.Version, inserted.Version);
        Assert.Equal("Omega", persisted.Name);
        Assert.Empty(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);
    }

    /// <summary>Verifies a reorder refreshes both changed and unchanged tracked rowversion values.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CoordinatedSaveRefreshesGeneratedTokensWithoutChangingAcceptanceSemantics(
        bool acceptAllChanges
    )
    {
        // Arrange
        await using var context = await _fixture.ResetAsync();
        var tree = context
            .NestedSet<SqlServerVersionNode>()
            .ForScope(1);

        var root = new SqlServerVersionNode { Name = "Root" };
        var first = new SqlServerVersionNode { Name = "Alpha" };
        var second = new SqlServerVersionNode { Name = "Middle" };
        await tree.InsertRootAsync(root, Guid.Empty, CancellationToken.None);
        await tree.InsertChildAsync(first, root.Id, CancellationToken.None);
        await tree.InsertChildAsync(second, root.Id, CancellationToken.None);
        var tracked = await context
            .Set<SqlServerVersionNode>()
            .Where(node => node.ParentId == root.Id)
            .OrderBy(node => node.Name)
            .ToArrayAsync(CancellationToken.None);

        var originalToken = tracked[0]
            .Version
            .ToArray();

        var peerToken = tracked[1]
            .Version
            .ToArray();

        tracked[0].Name = "Zulu";

        // Act
        await context.SaveChangesAsync(acceptAllChanges, CancellationToken.None);

        // Assert
        var currentToken = context
            .Entry(tracked[0])
            .Property(node => node.Version)
            .CurrentValue;
        Assert.NotEqual(originalToken, currentToken);
        Assert.NotEqual(peerToken, tracked[1].Version);
        Assert.Equal(
            acceptAllChanges ? EntityState.Unchanged : EntityState.Modified,
            context.Entry(tracked[0])
                .State);
        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(tracked[1])
                .State);
        Assert.Equal(
            acceptAllChanges ? "Zulu" : "Alpha",
            context
                .Entry(tracked[0])
                .Property(node => node.Name)
                .OriginalValue);
        var persisted = await context
            .Set<SqlServerVersionNode>()
            .AsNoTracking()
            .Where(node => node.ParentId == root.Id)
            .OrderBy(node => node.Left)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(["Middle", "Zulu"], persisted.Select(node => node.Name));
        Assert.Equal(persisted[0].Version, tracked[1].Version);
        Assert.Equal(persisted[1].Version, currentToken);
        Assert.Equal(acceptAllChanges ? currentToken : originalToken, tracked[0].Version);
    }
}

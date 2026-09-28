namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies native-key refresh for tracked entries that spell a database-equal scope differently.</summary>
[Collection("Model compatibility")]
public abstract class ScopeAliasRefreshTests : ProviderTest
{
    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Shares one provider database with the other mapping-compatibility tests.</summary>
    protected ScopeAliasRefreshTests(
        IProviderFixture<ModelCompatibilityDatabase> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>A scalar NodeKey refreshes its tracked alias without comparing scope values in CLR.</summary>
    [Fact]
    public async Task ScalarIdentityRefreshesTrackedScopeAliasAsync()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<ScalarScopeAliasContext>(
            Engine,
            static options => new ScalarScopeAliasContext(options));

        await SeedAsync(context, "tenant-a");
        var alias = await AttachAliasAsync(context, "tenant-a", "TENANT-A", 2);
        alias.Name = "Zulu";

        // Act
        await context.SaveNestedSetChangesAsync(
            true,
            token => context.SaveChangesAsync(false, token),
            CancellationToken.None);

        // Assert
        Assert.Equal((6L, 7L, 2L), (alias.Left, alias.Right, alias.Position));
    }

    /// <summary>A scope-qualified NodeKey resolves the tracked alias in SQL and keeps other scopes separate.</summary>
    [Fact]
    public async Task ScopedIdentityRefreshesTrackedScopeAliasAsync()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<ScopedScopeAliasContext>(
            Engine,
            static options => new ScopedScopeAliasContext(options));

        await SeedAsync(context, "tenant-b");
        await SeedAsync(context, "tenant-c");
        var alias = await AttachAliasAsync(context, "tenant-b", "TENANT-B", 2);
        var other = await context
            .Set<ScopeAliasNode>()
            .Where(node => node.Scope == "tenant-c")
            .ToDictionaryAsync(node => node.Id, CancellationToken.None);

        // WHY: The scopes reorder differently, so a row adopted from the other scope has other bounds.
        alias.Name = "Zulu";
        other[4].Name = "Aaaa";

        // Act
        await context.SaveNestedSetChangesAsync(
            true,
            token => context.SaveChangesAsync(false, token),
            CancellationToken.None);

        // Assert
        Assert.Equal((6L, 7L, 2L), (alias.Left, alias.Right, alias.Position));
        Assert.Equal((2L, 3L, 0L), (other[4].Left, other[4].Right, other[4].Position));
        Assert.Equal((4L, 5L, 1L), (other[2].Left, other[2].Right, other[2].Position));
        Assert.Equal((6L, 7L, 2L), (other[3].Left, other[3].Right, other[3].Position));
    }

    /// <summary>Creates one root with Alpha, Bravo, and Charlie children in rule order.</summary>
    private static async Task SeedAsync(
        DbContext context,
        string scope
    )
    {
        var hierarchy = context
            .NestedSet<ScopeAliasNode>()
            .ForScope(scope);

        await hierarchy.InsertRootAsync(
            new ScopeAliasNode
            {
                Id = 1,
                Name = "Root",
            },
            Guid.NewGuid(),
            CancellationToken.None);

        await hierarchy.InsertChildAsync(
            new ScopeAliasNode
            {
                Id = 2,
                Name = "Alpha",
            },
            1,
            CancellationToken.None);

        await hierarchy.InsertChildAsync(
            new ScopeAliasNode
            {
                Id = 3,
                Name = "Bravo",
            },
            1,
            CancellationToken.None);

        await hierarchy.InsertChildAsync(
            new ScopeAliasNode
            {
                Id = 4,
                Name = "Charlie",
            },
            1,
            CancellationToken.None);

        context.ChangeTracker.Clear();
    }

    /// <summary>Tracks a persisted row under a different spelling of its database-equal scope.</summary>
    private static async Task<ScopeAliasNode> AttachAliasAsync(
        DbContext context,
        string scope,
        string alias,
        int id
    )
    {
        var persisted = await context
            .Set<ScopeAliasNode>()
            .AsNoTracking()
            .SingleAsync(node => node.Scope == scope && node.Id == id, CancellationToken.None);

        var tracked = persisted.WithScope(alias);
        context.Attach(tracked);

        return tracked;
    }
}

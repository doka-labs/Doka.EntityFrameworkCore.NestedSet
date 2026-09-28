namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingRefreshTests
{
    /// <summary>Bounds refresh commands independently of foreign scopes and correlates native string aliases.</summary>
    [Fact]
    public async Task OneRenameAmongFiveThousandStringKeysRefreshesOnlyItsScope()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        var extensions = setup
            .GetService<IDbContextOptions>()
            .Extensions
            .ToDictionary(extension => extension.GetType());

        var options = new DbContextOptions<DbContext>(extensions);
        await using var seed = new RefreshScopeSeedContext(options);
        await seed
            .GetService<IRelationalDatabaseCreator>()
            .CreateTablesAsync(CancellationToken.None);

        var input = Enumerable
            .Range(1, 50)
            .SelectMany(CreateRefreshScopeNodes)
            .ToArray();

        await seed.AddRangeAsync(input, CancellationToken.None);
        await seed.SavePrecomputedHierarchyAsync(CancellationToken.None);
        var probe = new RefreshScopeProbe();
        await using var context = new RefreshScopeContext(
            new DbContextOptionsBuilder(options)
                .ConfigureTestWarnings()
                .AddInterceptors(probe)
                .Options);

        var tracked = await context
            .Set<RefreshScopeNode>()
            .Where(node => node.ParentId != null)
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        var renamed = tracked[0];
        var alias = CopyAlias(tracked[50], Engine != "Sqlite");
        alias.Left = 9999;
        alias.Right = 10000;
        context.Attach(alias);
        var unrelated = tracked[^1];
        var original = (unrelated.Left, unrelated.Right, unrelated.Position);
        unrelated.Payload = "pending foreign payload";

        // WHY: Lowercase z sorts after the seeded lowercase names under binary and linguistic collations.
        renamed.Name = "zzzz";
        probe.Commands.Clear();

        // Act
        var saved = await context.SaveChangesAsync(false, CancellationToken.None);

        // Assert
        Assert.Equal(2, saved);
        Assert.Equal((200, 201, 99), (renamed.Left, renamed.Right, renamed.Position));
        Assert.Equal((100, 101, 49), (alias.Left, alias.Right, alias.Position));
        Assert.Equal(
            (alias.Left, alias.Right, alias.Position),
            (tracked[50].Left, tracked[50].Right, tracked[50].Position));
        Assert.Equal(original, (unrelated.Left, unrelated.Right, unrelated.Position));
        Assert.True(
            context
                .Entry(unrelated)
                .Property(node => node.Payload)
                .IsModified);
        Assert.True(
            context
                .Entry(renamed)
                .Property(node => node.Name)
                .IsModified);
        var lastWrite = probe.Commands.FindLastIndex(command => command
            .TrimStart()
            .StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase));

        Assert.True(lastWrite >= 0);
        var refresh = probe
            .Commands
            .Skip(lastWrite + 1)
            .ToArray();
        Assert.InRange(refresh.Length, 1, 3);
        Assert.All(
            refresh,
            sql =>
            {
                Assert.DoesNotContain("Payload", sql, StringComparison.Ordinal);
                Assert.DoesNotContain("Name", sql, StringComparison.Ordinal);
            });
        Assert.Equal(
            5001,
            context
                .ChangeTracker
                .Entries<RefreshScopeNode>()
                .Count());
        await using var verification = new RefreshScopeSeedContext(options);
        Assert.Equal(
            "pending foreign payload",
            await verification
                .Set<RefreshScopeNode>()
                .Where(node => node.Id == unrelated.Id)
                .Select(node => node.Payload)
                .SingleAsync(CancellationToken.None));
    }

    /// <summary>Preserves distinct database scopes despite a broader application CLR comparer.</summary>
    [Fact]
    public async Task BroaderScopeComparerDoesNotOmitASeparateDatabaseScope()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        var extensions = setup
            .GetService<IDbContextOptions>()
            .Extensions
            .ToDictionary(extension => extension.GetType());

        var options = new DbContextOptions<DbContext>(extensions);
        await using var context = new ScopeComparerContext(options);
        await context
            .GetService<IRelationalDatabaseCreator>()
            .CreateTablesAsync(CancellationToken.None);

        var seedOptions = new DbContextOptionsBuilder(options)
            .ConfigureTestWarnings()
            .UseModel(context.Model)
            .Options;

        await using var seed = new DbContext(seedOptions);
        var treeId = Guid.NewGuid();
        await seed.AddRangeAsync(
            [
                new RefreshScopeNode
                {
                    Id = "upper-root",
                    Scope = "A",
                    TreeId = treeId,
                    Name = "Root",
                    Left = 1,
                    Right = 6,
                },
                new RefreshScopeNode
                {
                    Id = "one",
                    Scope = "A",
                    TreeId = treeId,
                    ParentId = "upper-root",
                    Name = "Alpha",
                    Left = 2,
                    Right = 3,
                    Depth = 1,
                },
                new RefreshScopeNode
                {
                    Id = "two",
                    Scope = "A",
                    TreeId = treeId,
                    ParentId = "upper-root",
                    Name = "Bravo",
                    Left = 4,
                    Right = 5,
                    Depth = 1,
                    Position = 1,
                },
                new RefreshScopeNode
                {
                    Id = "lower-root",
                    Scope = "a",
                    TreeId = treeId,
                    Name = "Root",
                    Left = 1,
                    Right = 6,
                },
                new RefreshScopeNode
                {
                    Id = "three",
                    Scope = "a",
                    TreeId = treeId,
                    ParentId = "lower-root",
                    Name = "Alpha",
                    Left = 2,
                    Right = 3,
                    Depth = 1,
                },
                new RefreshScopeNode
                {
                    Id = "four",
                    Scope = "a",
                    TreeId = treeId,
                    ParentId = "lower-root",
                    Name = "Bravo",
                    Left = 4,
                    Right = 5,
                    Depth = 1,
                    Position = 1,
                },
            ],
            CancellationToken.None);

        await seed.SavePrecomputedHierarchyAsync(CancellationToken.None);
        var comparer =
            context.Model.FindEntityType(typeof(RefreshScopeNode))!.FindProperty(nameof(RefreshScopeNode.Scope))!
                .GetKeyValueComparer();

        var rows = await context
            .Set<RefreshScopeNode>()
            .ToArrayAsync(CancellationToken.None);

        rows.Single(node => node.Id == "one").Name = "Zulu";
        rows.Single(node => node.Id == "three").Name = "Zulu";

        // Act
        var saved = await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.True(comparer.Equals("A", "a"));
        Assert.Equal(2, saved);
        Assert.Equal<string>(
            ["two", "one"],
            await seed
                .Set<RefreshScopeNode>()
                .AsNoTracking()
                .Where(node => node.Scope == "A" && node.ParentId != null)
                .OrderBy(node => node.Left)
                .Select(node => node.Id)
                .ToArrayAsync(CancellationToken.None));
        Assert.Equal<string>(
            ["four", "three"],
            await seed
                .Set<RefreshScopeNode>()
                .AsNoTracking()
                .Where(node => node.Scope == "a" && node.ParentId != null)
                .OrderBy(node => node.Left)
                .Select(node => node.Id)
                .ToArrayAsync(CancellationToken.None));
        Assert.Equal(4, rows.Single(node => node.Id == "one").Left);
        Assert.Equal(4, rows.Single(node => node.Id == "three").Left);
    }

    /// <summary>
    ///     Seeds one untracked root and exactly one hundred measured sibling rows in each independent Scope.
    /// </summary>
    private static IEnumerable<RefreshScopeNode> CreateRefreshScopeNodes(
        int scope
    )
    {
        var scopeId = $"scope-{scope:D2}-\u00e9";
        var rootId = $"{scopeId}-root";
        var treeId = Guid.NewGuid();

        // WHY: Keeping the root outside the measured tracker preserves the original 5,000-child refresh workload.
        yield return new RefreshScopeNode
        {
            Id = rootId,
            Scope = scopeId,
            TreeId = treeId,
            Name = "Root",
            Left = 1,
            Right = 202,
        };

        for (var position = 0; position < 100; position++)
        {
            yield return new RefreshScopeNode
            {
                Id = $"{scopeId}-node-{position:D3}",
                Scope = scopeId,
                TreeId = treeId,
                ParentId = rootId,
                Name = $"node-{position:D3}",
                Left = (position * 2) + 2,
                Right = (position * 2) + 3,
                Depth = 1,
                Position = position,
            };
        }
    }

    /// <summary>Creates a second CLR identity that native case-insensitive equality resolves to the same row.</summary>
    private static RefreshScopeNode CopyAlias(
        RefreshScopeNode node,
        bool accentInsensitive
    ) => new()
    {
        Id = Alias(node.Id, accentInsensitive),
        Scope = Alias(node.Scope, accentInsensitive),
        TreeId = node.TreeId,
        ParentId = node.ParentId,
        Name = node.Name,
        Payload = node.Payload,
        Left = node.Left,
        Right = node.Right,
        Depth = node.Depth,
        Position = node.Position,
    };

    /// <summary>Exercises accent aliases only where the configured native collation supports them.</summary>
    private static string Alias(
        string value,
        bool accentInsensitive
    ) => accentInsensitive
        ? value
            .Replace("\u00e9", "e", StringComparison.Ordinal)
            .ToUpperInvariant()
        : value
            .ToUpperInvariant()
            .Replace("\u00c9", "\u00e9", StringComparison.Ordinal);
}

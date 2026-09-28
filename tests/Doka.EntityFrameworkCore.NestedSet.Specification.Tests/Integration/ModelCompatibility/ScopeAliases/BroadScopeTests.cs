namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies that broad domain equality cannot merge distinct stored scope values.</summary>
[Collection("Model compatibility")]
public abstract class BroadScopeTests : ProviderTest
{
    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Shares provider databases with the other mapping-compatibility tests.</summary>
    protected BroadScopeTests(
        IProviderFixture<ModelCompatibilityDatabase> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Compares the stored representation instead of the value object's broad equality.</summary>
    [Fact]
    public void ProviderComparerSeparatesReferenceAliases()
    {
        // Arrange
        using var context = new BroadScopeContext(ModelCompatibilityDatabase.Options<BroadScopeContext>(Engine));

        var first = new BroadScope("A");
        var second = new BroadScope("a");
        var entityType = context.Model.FindEntityType(typeof(BroadScopeNode))!;
        var property = entityType.FindProperty(nameof(BroadScopeNode.Scope))!;
        var comparer = new NestedSetProviderComparer<BroadScope>(property);

        // Act
        var equal = comparer.Equals(first, second);

        // Assert
        Assert.False(equal);
    }

    /// <summary>Reorders both provider-distinct scopes during one coordinated save.</summary>
    [Fact]
    public async Task SortedSaveKeepsStoredScopesSeparateAsync()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<BroadScopeContext>(
            Engine,
            static options => new BroadScopeContext(options));

        var firstTree = Guid.NewGuid();
        var secondTree = Guid.NewGuid();
        Seed(context, "A", firstTree);
        Seed(context, "a", secondTree);
        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();

        var tracked = await context
            .Set<BroadScopeNode>()
            .ToArrayAsync(CancellationToken.None);

        tracked.Single(node => node.Scope.Value == "A" && node.Id == 2).Name = "Zulu";
        tracked.Single(node => node.Scope.Value == "a" && node.Id == 2).Name = "Zulu";

        // Act
        await context.SaveNestedSetChangesAsync(
            true,
            token => context.SaveChangesAsync(false, token),
            CancellationToken.None);

        // Assert
        foreach (var scope in new[] { "A", "a" })
        {
            var children = tracked
                .Where(node => node.Scope.Value == scope && node.Depth == 1)
                .ToArray();

            Assert.Equal((4L, 5L, 1L), Bounds(children.Single(node => node.Id == 2)));
            Assert.Equal((2L, 3L, 0L), Bounds(children.Single(node => node.Id == 3)));
        }

        Assert.True(
            (await context
                .NestedSet<BroadScopeNode>()
                .ForScope(new BroadScope("A"))
                .InTree(firstTree)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
        Assert.True(
            (await context
                .NestedSet<BroadScopeNode>()
                .ForScope(new BroadScope("a"))
                .InTree(secondTree)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }

    /// <summary>Refreshes a moved branch without adopting a matching node key from another scope.</summary>
    [Fact]
    public async Task ParentSaveRefreshesOnlyItsStoredScopeAsync()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<BroadScopeContext>(
            Engine,
            static options => new BroadScopeContext(options));

        var firstTree = Guid.NewGuid();
        var secondTree = Guid.NewGuid();
        Seed(context, "B", firstTree);
        Seed(context, "b", secondTree);
        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();

        var tracked = await context
            .Set<BroadScopeNode>()
            .ToArrayAsync(CancellationToken.None);

        var moved = tracked.Single(node => node.Scope.Value == "B" && node.Id == 2);
        var foreign = tracked.Single(node => node.Scope.Value == "b" && node.Id == 2);
        moved.ParentId = 3;

        // Act
        await context.SaveNestedSetChangesAsync(
            true,
            token => context.SaveChangesAsync(false, token),
            CancellationToken.None);

        // Assert
        Assert.Equal((3L, 4L, 0L), Bounds(moved));
        Assert.Equal(2, moved.Depth);
        Assert.Equal((2L, 3L, 0L), Bounds(foreign));
        Assert.Equal(1, foreign.Depth);
        Assert.Equal(EntityState.Unchanged, context.Entry(foreign).State);
        Assert.True(
            (await context
                .NestedSet<BroadScopeNode>()
                .ForScope(new BroadScope("B"))
                .InTree(firstTree)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
        Assert.True(
            (await context
                .NestedSet<BroadScopeNode>()
                .ForScope(new BroadScope("b"))
                .InTree(secondTree)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }

    /// <summary>A save callback cannot redirect a single insert into a database-distinct scope.</summary>
    [Fact]
    public async Task SingleInsertRejectsCallbackScopeAliasAsync()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<BroadScopeContext>(
            Engine,
            static options => new BroadScopeContext(options));

        var input = new BroadScopeNode
        {
            Id = 21,
            Scope = new BroadScope("original"),
            Name = "Root",
            Left = 71,
            Right = 72,
        };

        context.SavingChanges += (_, _) => input.Scope = new BroadScope("c");

        // Act
        var error = await Record.ExceptionAsync(() => context
            .NestedSet<BroadScopeNode>()
            .ForScope(new BroadScope("C"))
            .InsertRootAsync(input, Guid.NewGuid(), CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidStructure, Assert.IsType<NestedSetException>(error).Code);
        Assert.Equal("original", input.Scope.Value);
        Assert.Equal(
            0,
            await context
                .Set<BroadScopeNode>()
                .CountAsync(node => node.Id == 21, CancellationToken.None));
    }

    /// <summary>A save callback cannot redirect a bulk import into a database-distinct scope.</summary>
    [Fact]
    public async Task BulkInsertRejectsCallbackScopeAliasAsync()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<BroadScopeContext>(
            Engine,
            static options => new BroadScopeContext(options));

        var input = new BroadScopeNode
        {
            Id = 22,
            Scope = new BroadScope("original"),
            Name = "Root",
            Left = 71,
            Right = 72,
        };

        var tree = new NestedSetTreeImport<BroadScopeNode, Guid>(
            Guid.NewGuid(),
            new NestedSetBranch<BroadScopeNode>(input));

        context.SavingChanges += (_, _) => input.Scope = new BroadScope("d");

        // Act
        var error = await Record.ExceptionAsync(() => context
            .NestedSet<BroadScopeNode>()
            .ForScope(new BroadScope("D"))
            .InsertForestAsync([tree], CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidImport, Assert.IsType<NestedSetException>(error).Code);
        Assert.Equal("original", input.Scope.Value);
        Assert.Equal(
            0,
            await context
                .Set<BroadScopeNode>()
                .CountAsync(node => node.Id == 22, CancellationToken.None));
    }

    /// <summary>Seeds one precomputed tree to isolate the save-time scope planner.</summary>
    private static void Seed(
        DbContext context,
        string scope,
        Guid treeId
    )
    {
        context.AddRange(
            new BroadScopeNode
            {
                Scope = new BroadScope(scope),
                Id = 1,
                TreeId = treeId,
                Name = "Root",
                Left = 1,
                Right = 6,
            },
            new BroadScopeNode
            {
                Scope = new BroadScope(scope),
                Id = 2,
                TreeId = treeId,
                ParentId = 1,
                Name = "Alpha",
                Left = 2,
                Right = 3,
                Depth = 1,
            },
            new BroadScopeNode
            {
                Scope = new BroadScope(scope),
                Id = 3,
                TreeId = treeId,
                ParentId = 1,
                Name = "Bravo",
                Left = 4,
                Right = 5,
                Depth = 1,
                Position = 1,
            });
    }

    /// <summary>Returns the structural coordinates relevant to sibling ordering.</summary>
    private static (long Left, long Right, long Position) Bounds(
        BroadScopeNode node
    ) => (node.Left, node.Right, node.Position);
}

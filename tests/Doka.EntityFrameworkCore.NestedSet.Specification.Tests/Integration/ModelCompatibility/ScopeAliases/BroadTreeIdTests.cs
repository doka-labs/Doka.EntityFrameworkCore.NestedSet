namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies provider-distinct tree identities throughout saves and managed inserts.</summary>
[Collection("Model compatibility")]
public abstract class BroadTreeIdTests : ProviderTest
{
    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Shares provider databases with the other mapping-compatibility tests.</summary>
    protected BroadTreeIdTests(
        IProviderFixture<ModelCompatibilityDatabase> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Compares converted TreeIds by stored value in typed and runtime guards.</summary>
    [DatabaseIndependent]
    [Fact]
    public void ProviderComparerSeparatesTreeAliases()
    {
        // Arrange
        using var context = new BroadTreeIdContext(ModelCompatibilityDatabase.Options<BroadTreeIdContext>(Engine));
        var property = context.Model.FindEntityType(typeof(BroadTreeIdNode))!.FindProperty(nameof(BroadTreeIdNode.TreeId))!;

        var first = new BroadTreeId("E");
        var second = new BroadTreeId("e");
        var comparer = new NestedSetProviderComparer<BroadTreeId>(property);

        // Act
        var typedEqual = comparer.Equals(first, second);
        var runtimeEqual = NestedSetProviderComparer.Matches(property, first, second);

        // Assert
        Assert.False(typedEqual);
        Assert.False(runtimeEqual);
    }

    /// <summary>Registers both stored identities even when their domain values compare equal.</summary>
    [Fact]
    public async Task RegistryRetainsBothStoredTreeIdsAsync()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<BroadTreeIdContext>(
            Engine,
            static options => new BroadTreeIdContext(options));

        Seed(context, "E", 31);
        Seed(context, "e", 34);
        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        var entityType = context.Model.FindEntityType(typeof(BroadTreeIdNode))!;
        var registry = NestedSetTreeRegistryMapping.For(entityType);

        // Act
        var identities = await context
            .Set<NestedSetTreeRegistry>(registry.Registry.Name)
            .AsNoTracking()
            .Select(row => EF.Property<BroadTreeId>(row, NestedSetTreeRegistryMetadata.TreeId))
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(
            ["E", "e"],
            identities
                .Select(identity => identity.Value)
                .Where(value => value is "E" or "e")
                .OrderBy(value => value, StringComparer.Ordinal));
    }

    /// <summary>Reorders two trees whose domain identities compare equal.</summary>
    [Fact]
    public async Task SortedSaveKeepsStoredTreesSeparateAsync()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<BroadTreeIdContext>(
            Engine,
            static options => new BroadTreeIdContext(options));

        Seed(context, "A", 1);
        Seed(context, "a", 4);
        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();

        var tracked = await context
            .Set<BroadTreeIdNode>()
            .ToArrayAsync(CancellationToken.None);

        tracked.Single(node => node.Id == 2).Name = "Zulu";
        tracked.Single(node => node.Id == 5).Name = "Zulu";

        // Act
        await context.SaveNestedSetChangesAsync(
            true,
            token => context.SaveChangesAsync(false, token),
            CancellationToken.None);

        // Assert
        Assert.Equal((4L, 5L, 1L), Bounds(tracked.Single(node => node.Id == 2)));
        Assert.Equal((2L, 3L, 0L), Bounds(tracked.Single(node => node.Id == 3)));
        Assert.Equal((4L, 5L, 1L), Bounds(tracked.Single(node => node.Id == 5)));
        Assert.Equal((2L, 3L, 0L), Bounds(tracked.Single(node => node.Id == 6)));
        Assert.True(
            (await context
                .NestedSet<BroadTreeIdNode>()
                .InTree(new BroadTreeId("A"))
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
        Assert.True(
            (await context
                .NestedSet<BroadTreeIdNode>()
                .InTree(new BroadTreeId("a"))
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }

    /// <summary>Plans parent moves independently for two stored tree identities.</summary>
    [Fact]
    public async Task ParentSaveKeepsStoredTreesSeparateAsync()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<BroadTreeIdContext>(
            Engine,
            static options => new BroadTreeIdContext(options));

        Seed(context, "B", 11);
        Seed(context, "b", 14);
        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();

        var tracked = await context
            .Set<BroadTreeIdNode>()
            .ToArrayAsync(CancellationToken.None);

        tracked.Single(node => node.Id == 12).ParentId = 13;
        tracked.Single(node => node.Id == 15).ParentId = 16;

        // Act
        await context.SaveNestedSetChangesAsync(
            true,
            token => context.SaveChangesAsync(false, token),
            CancellationToken.None);

        // Assert
        Assert.Equal((3L, 4L, 0L), Bounds(tracked.Single(node => node.Id == 12)));
        Assert.Equal((3L, 4L, 0L), Bounds(tracked.Single(node => node.Id == 15)));
        Assert.True(
            (await context
                .NestedSet<BroadTreeIdNode>()
                .InTree(new BroadTreeId("B"))
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
        Assert.True(
            (await context
                .NestedSet<BroadTreeIdNode>()
                .InTree(new BroadTreeId("b"))
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }

    /// <summary>A save callback cannot redirect a single insert to an unlocked stored TreeId.</summary>
    [Fact]
    public async Task SingleInsertRejectsCallbackTreeAliasAsync()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<BroadTreeIdContext>(
            Engine,
            static options => new BroadTreeIdContext(options));

        var input = new BroadTreeIdNode
        {
            Id = 21,
            TreeId = new BroadTreeId("original"),
            Name = "Root",
            Left = 71,
            Right = 72,
        };

        context.SavingChanges += (_, _) => input.TreeId = new BroadTreeId("c");

        // Act
        var error = await Record.ExceptionAsync(() => context
            .NestedSet<BroadTreeIdNode>()
            .InsertRootAsync(input, new BroadTreeId("C"), CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidStructure, Assert.IsType<NestedSetException>(error).Code);
        Assert.Equal("original", input.TreeId.Value);
        Assert.Equal(
            0,
            await context
                .Set<BroadTreeIdNode>()
                .CountAsync(node => node.Id == 21, CancellationToken.None));
    }

    /// <summary>A save callback cannot redirect a bulk import to an unlocked stored TreeId.</summary>
    [Fact]
    public async Task BulkInsertRejectsCallbackTreeAliasAsync()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<BroadTreeIdContext>(
            Engine,
            static options => new BroadTreeIdContext(options));

        var input = new BroadTreeIdNode
        {
            Id = 22,
            TreeId = new BroadTreeId("original"),
            Name = "Root",
            Left = 71,
            Right = 72,
        };

        var tree = new NestedSetTreeImport<BroadTreeIdNode, BroadTreeId>(
            new BroadTreeId("D"),
            new NestedSetBranch<BroadTreeIdNode>(input));

        context.SavingChanges += (_, _) => input.TreeId = new BroadTreeId("d");

        // Act
        var error = await Record.ExceptionAsync(() => context
            .NestedSet<BroadTreeIdNode>()
            .InsertForestAsync([tree], CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidImport, Assert.IsType<NestedSetException>(error).Code);
        Assert.Equal("original", input.TreeId.Value);
        Assert.Equal(
            0,
            await context
                .Set<BroadTreeIdNode>()
                .CountAsync(node => node.Id == 22, CancellationToken.None));
    }

    /// <summary>Seeds one root with two ordered children and provider-distinct tree coordinates.</summary>
    private static void Seed(
        DbContext context,
        string treeId,
        int root
    )
    {
        context.AddRange(
            new BroadTreeIdNode
            {
                Id = root,
                TreeId = new BroadTreeId(treeId),
                Name = "Root",
                Left = 1,
                Right = 6,
            },
            new BroadTreeIdNode
            {
                Id = root + 1,
                TreeId = new BroadTreeId(treeId),
                ParentId = root,
                Name = "Alpha",
                Left = 2,
                Right = 3,
                Depth = 1,
            },
            new BroadTreeIdNode
            {
                Id = root + 2,
                TreeId = new BroadTreeId(treeId),
                ParentId = root,
                Name = "Bravo",
                Left = 4,
                Right = 5,
                Depth = 1,
                Position = 1,
            });
    }

    /// <summary>Returns the structural coordinates relevant to sibling ordering.</summary>
    private static (long Left, long Right, long Position) Bounds(
        BroadTreeIdNode node
    ) => (node.Left, node.Right, node.Position);
}

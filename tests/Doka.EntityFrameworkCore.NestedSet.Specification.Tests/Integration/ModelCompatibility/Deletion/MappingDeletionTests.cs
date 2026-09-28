namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies deletion across the physical tables of supported EF mapping strategies.</summary>
[Collection("Model compatibility")]
public abstract class MappingDeletionTests : ProviderTest
{
    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Shares the relational provider databases with the other mapping tests.</summary>
    protected MappingDeletionTests(
        IProviderFixture<ModelCompatibilityDatabase> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Provides each deletion operation independently of the owning provider fixture.</summary>
    public static IEnumerable<TheoryDataRow<MappingDeleteOperation>> Cases()
    {
        yield return new TheoryDataRow<MappingDeleteOperation>(MappingDeleteOperation.Node);
        yield return new TheoryDataRow<MappingDeleteOperation>(MappingDeleteOperation.Subtree);
        yield return new TheoryDataRow<MappingDeleteOperation>(MappingDeleteOperation.Tree);
    }

    /// <summary>Deletes TPT rows from both derived and base tables without leaving fragments behind.</summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task TptDeletesEveryFragment(
        MappingDeleteOperation operation
    )
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<TptContext>(
            Engine,
            static options => new TptContext(options));

        var scenario = await MappingDeleteTestSupport.SeedAsync(
            context,
            static (id, name) => new TptFolderNode
            {
                Id = id,
                Name = name,
                FolderKind = "Folder",
            },
            null);

        // Act
        await DeleteAsync<InheritanceNode>(context, scenario, operation);

        // Assert
        await VerifyAsync<TptFolderNode>(context, scenario, operation, "TptNodes", "TptFolders");
    }

    /// <summary>Deletes only the matching derived row when one TPT tree mixes concrete node types.</summary>
    [Fact]
    public async Task TptDeletePromotesChildrenAcrossDerivedTypes()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<TptContext>(
            Engine,
            static options => new TptContext(options));

        var hierarchy = context.NestedSet<InheritanceNode>();
        var treeId = Guid.NewGuid();
        var seed = 100_000 + (MappingDeleteTestSupport.NextSequence() * 10);
        await hierarchy.InsertRootAsync(
            new TptFolderNode
            {
                Id = seed,
                Name = "Root",
            },
            treeId,
            CancellationToken.None);

        await hierarchy.InsertChildAsync(
            new TptMetricNode
            {
                Id = seed + 1,
                Name = "Branch",
            },
            seed,
            CancellationToken.None);

        await hierarchy.InsertChildAsync(
            new TptFolderNode
            {
                Id = seed + 2,
                Name = "Leaf",
            },
            seed + 1,
            CancellationToken.None);

        await hierarchy.InsertChildAsync(
            new TptMetricNode
            {
                Id = seed + 3,
                Name = "Sibling",
            },
            seed,
            CancellationToken.None);

        // Act
        await hierarchy.DeleteAsync(seed + 1, CancellationToken.None);

        // Assert
        Assert.Equal(3, await MappingDeleteTestSupport.CountRowsAsync(context, "TptNodes", seed));
        Assert.Equal(2, await MappingDeleteTestSupport.CountRowsAsync(context, "TptFolders", seed));
        Assert.Equal(1, await MappingDeleteTestSupport.CountRowsAsync(context, "TptMetrics", seed));
        Assert.True(
            (await hierarchy
                .InTree(treeId)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }

    /// <summary>Deletes a shared table row once and retains unrelated payload rows.</summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task TableSplitDeletesSharedRows(
        MappingDeleteOperation operation
    )
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<TableSplitContext>(
            Engine,
            static options => new TableSplitContext(options));

        var scenario = await MappingDeleteTestSupport.SeedAsync(
            context,
            static (id, name) => new TableSplitNode
            {
                Id = id,
                Name = name,
            },
            static async (database, id) =>
            {
                database
                    .Set<TableSplitPayload>()
                    .Add(
                        new TableSplitPayload
                        {
                            Id = id + 1,
                            Description = "Branch",
                        });

                database
                    .Set<TableSplitPayload>()
                    .Add(
                        new TableSplitPayload
                        {
                            Id = id + 3,
                            Description = "Sibling",
                        });

                await database.SaveChangesAsync(CancellationToken.None);
            });

        // Act
        await DeleteAsync<TableSplitNode>(context, scenario, operation);

        // Assert
        await VerifyAsync<TableSplitNode>(context, scenario, operation, "TableSplitNodes");
        var payload = await context
            .Set<TableSplitPayload>()
            .AsNoTracking()
            .Where(value => value.Id >= scenario.Seed && value.Id <= scenario.Seed + 4)
            .Select(value => value.Id)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(operation == MappingDeleteOperation.Tree ? [] : [scenario.Seed + 3], payload);
    }

    /// <summary>Deletes both entity-splitting fragments in foreign-key-safe order.</summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task EntitySplitDeletesEveryFragment(
        MappingDeleteOperation operation
    )
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<EntitySplitContext>(
            Engine,
            static options => new EntitySplitContext(options));

        var scenario = await MappingDeleteTestSupport.SeedAsync(
            context,
            static (id, name) => new EntitySplitNode
            {
                Id = id,
                Name = name,
            },
            null);

        // Act
        await DeleteAsync<EntitySplitNode>(context, scenario, operation);

        // Assert
        await VerifyAsync<EntitySplitNode>(context, scenario, operation, "EntitySplitPayload", "EntitySplitStructure");
    }

    /// <summary>Processes more than one key batch without leaving entity-splitting fragments behind.</summary>
    [Fact]
    public async Task EntitySplitTreeDeleteCrossesBatchBoundary()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<EntitySplitContext>(
            Engine,
            static options => new EntitySplitContext(options));

        var hierarchy = context.NestedSet<EntitySplitNode>();
        var treeId = Guid.NewGuid();
        var seed = 1_000_000 + MappingDeleteTestSupport.NextSequence() * 200;
        var children = Enumerable
            .Range(0, 130)
            .Select(offset => new NestedSetBranch<EntitySplitNode>(
                new EntitySplitNode
                {
                    Id = seed + 2 + offset,
                    Name = $"Leaf {offset}",
                }))
            .ToArray();

        await hierarchy.InsertRootAsync(
            new EntitySplitNode
            {
                Id = seed,
                Name = "Root",
            },
            treeId,
            CancellationToken.None);

        await hierarchy.InsertSubtreeAsync(
            new NestedSetBranch<EntitySplitNode>(
                new EntitySplitNode
                {
                    Id = seed + 1,
                    Name = "Branch",
                },
                children),
            seed,
            CancellationToken.None);

        // Act
        await hierarchy.DeleteTreeAsync(treeId, CancellationToken.None);

        // Assert
        Assert.Equal(0, await MappingDeleteTestSupport.CountRowsAsync(context, "EntitySplitPayload", seed, 131));
        Assert.Equal(0, await MappingDeleteTestSupport.CountRowsAsync(context, "EntitySplitStructure", seed, 131));
    }

    private static async Task DeleteAsync<TNode>(
        DbContext context,
        MappingDeleteScenario scenario,
        MappingDeleteOperation operation
    )
        where TNode : class
    {
        var hierarchy = context.NestedSet<TNode>();

        switch (operation)
        {
            case MappingDeleteOperation.Node:
                await hierarchy.DeleteAsync(scenario.Seed + 1, CancellationToken.None);
                break;
            case MappingDeleteOperation.Subtree:
                await hierarchy.DeleteSubtreeAsync(scenario.Seed + 1, CancellationToken.None);
                break;
            case MappingDeleteOperation.Tree:
                await hierarchy.DeleteTreeAsync(scenario.TargetTree, CancellationToken.None);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private static async Task VerifyAsync<TNode>(
        DbContext context,
        MappingDeleteScenario scenario,
        MappingDeleteOperation operation,
        params string[] tables
    )
        where TNode : class
    {
        var hierarchy = context.NestedSet<TNode>();
        var seed = scenario.Seed;

        var actual = await context
            .Set<TNode>()
            .AsNoTracking()
            .Where(node => EF.Property<int>(node, "Id") >= seed && EF.Property<int>(node, "Id") <= seed + 4)
            .Select(node => EF.Property<int>(node, "Id"))
            .OrderBy(id => id)
            .ToArrayAsync(CancellationToken.None);

        int[] expected = operation switch
        {
            MappingDeleteOperation.Node => [seed, seed + 2, seed + 3, seed + 4],
            MappingDeleteOperation.Subtree => [seed, seed + 3, seed + 4],
            MappingDeleteOperation.Tree => [seed + 4],
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        Assert.Equal(expected, actual);
        Assert.True(
            (await hierarchy
                .InTree(scenario.OtherTree)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);

        if (operation != MappingDeleteOperation.Tree)
        {
            Assert.True(
                (await hierarchy
                    .InTree(scenario.TargetTree)
                    .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
        }

        foreach (var table in tables)
        {
            Assert.Equal(expected.Length, await MappingDeleteTestSupport.CountRowsAsync(context, table, seed));
        }
    }
}

/// <summary>Isolates the generated keys and tree identities of one deletion case.</summary>
internal readonly record struct MappingDeleteScenario(
    int Seed,
    Guid TargetTree,
    Guid OtherTree
);

/// <summary>Names the three public deletion operations covered by mapping tests.</summary>
public enum MappingDeleteOperation
{
    /// <summary>Deletes one node and promotes its children.</summary>
    Node,

    /// <summary>Deletes one subtree.</summary>
    Subtree,

    /// <summary>Deletes a complete tree.</summary>
    Tree,
}

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies deletion across the physical tables of supported EF mapping strategies.</summary>
[Collection("Model compatibility")]
public abstract partial class MappingDeletionTests : ProviderTest
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
    public async Task DeleteAsync_Tpt_DeletesEveryFragment(
        MappingDeleteOperation operation
    )
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<TptContext>(
            Engine,
            static options => new TptContext(options),
            cancellationToken: TestContext.Current.CancellationToken);

        var scenario = await MappingDeleteTestSupport.SeedAsync(
            context,
            static (id, name) => new TptFolderNode
            {
                Id = id,
                Name = name,
                FolderKind = "Folder",
            },
            null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await DeleteAsync<InheritanceNode>(
            context,
            scenario,
            operation,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        await VerifyAsync<TptFolderNode>(
            context,
            scenario,
            operation,
            cancellationToken: TestContext.Current.CancellationToken,
            "TptNodes",
            "TptFolders");
    }

    /// <summary>Deletes only the matching derived row when one TPT tree mixes concrete node types.</summary>
    [Fact]
    public async Task DeleteAsync_TptDelete_PromotesChildrenAcrossDerivedTypes()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<TptContext>(
            Engine,
            static options => new TptContext(options),
            cancellationToken: TestContext.Current.CancellationToken);

        var hierarchy = context.NestedSet<InheritanceNode>();
        var treeId = Guid.NewGuid();
        var seed = 100_000 + MappingDeleteTestSupport.NextSequence() * 10;
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
        Assert.Equal(
            3,
            await MappingDeleteTestSupport.CountRowsAsync(
                context,
                "TptNodes",
                seed,
                cancellationToken: TestContext.Current.CancellationToken,
                lastOffset: 4));

        Assert.Equal(
            2,
            await MappingDeleteTestSupport.CountRowsAsync(
                context,
                "TptFolders",
                seed,
                cancellationToken: TestContext.Current.CancellationToken,
                lastOffset: 4));

        Assert.Equal(
            1,
            await MappingDeleteTestSupport.CountRowsAsync(
                context,
                "TptMetrics",
                seed,
                cancellationToken: TestContext.Current.CancellationToken,
                lastOffset: 4));

        Assert.True(
            (await hierarchy
                .InTree(treeId)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }

    /// <summary>Deletes a shared table row once and retains unrelated payload rows.</summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task DeleteAsync_TableSplit_DeletesSharedRows(
        MappingDeleteOperation operation
    )
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<TableSplitContext>(
            Engine,
            static options => new TableSplitContext(options),
            cancellationToken: TestContext.Current.CancellationToken);

        var scenario = await MappingDeleteTestSupport.SeedAsync(
            context,
            static (id, name) => new TableSplitNode
            {
                Id = id,
                Name = name,
            },
            static async (database, id, cancellationToken) =>
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

                await database.SaveChangesAsync(cancellationToken);
            },
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await DeleteAsync<TableSplitNode>(
            context,
            scenario,
            operation,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        await VerifyAsync<TableSplitNode>(
            context,
            scenario,
            operation,
            cancellationToken: TestContext.Current.CancellationToken,
            "TableSplitNodes");

        var payload = await context
            .Set<TableSplitPayload>()
            .AsNoTracking()
            .Where(value => value.Id >= scenario.Seed && value.Id <= scenario.Seed + 4)
            .Select(value => value.Id)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(
            operation == MappingDeleteOperation.Tree
                ? []
                : [scenario.Seed + 3],
            payload);
    }

    /// <summary>Deletes both entity-splitting fragments in foreign-key-safe order.</summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task DeleteAsync_EntitySplit_DeletesEveryFragment(
        MappingDeleteOperation operation
    )
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<EntitySplitContext>(
            Engine,
            static options => new EntitySplitContext(options),
            cancellationToken: TestContext.Current.CancellationToken);

        var scenario = await MappingDeleteTestSupport.SeedAsync(
            context,
            static (id, name) => new EntitySplitNode
            {
                Id = id,
                Name = name,
            },
            null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await DeleteAsync<EntitySplitNode>(
            context,
            scenario,
            operation,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        await VerifyAsync<EntitySplitNode>(
            context,
            scenario,
            operation,
            cancellationToken: TestContext.Current.CancellationToken,
            "EntitySplitPayload",
            "EntitySplitStructure");
    }

    /// <summary>Processes more than one key batch without leaving entity-splitting fragments behind.</summary>
    [Fact]
    public async Task DeleteTreeAsync_EntitySplitTreeDelete_CrossesBatchBoundary()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<EntitySplitContext>(
            Engine,
            static options => new EntitySplitContext(options),
            cancellationToken: TestContext.Current.CancellationToken);

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
        Assert.Equal(
            0,
            await MappingDeleteTestSupport.CountRowsAsync(
                context,
                "EntitySplitPayload",
                seed,
                cancellationToken: TestContext.Current.CancellationToken,
                lastOffset: 131));

        Assert.Equal(
            0,
            await MappingDeleteTestSupport.CountRowsAsync(
                context,
                "EntitySplitStructure",
                seed,
                cancellationToken: TestContext.Current.CancellationToken,
                lastOffset: 131));
    }

    private static async Task DeleteAsync<TNode>(
        DbContext context,
        MappingDeleteScenario scenario,
        MappingDeleteOperation operation,
        CancellationToken cancellationToken
    )
        where TNode : class
    {
        var hierarchy = context.NestedSet<TNode>();

        switch (operation)
        {
            case MappingDeleteOperation.Node:
                await hierarchy.DeleteAsync(scenario.Seed + 1, cancellationToken);
                break;
            case MappingDeleteOperation.Subtree:
                await hierarchy.DeleteSubtreeAsync(scenario.Seed + 1, cancellationToken);
                break;
            case MappingDeleteOperation.Tree:
                await hierarchy.DeleteTreeAsync(scenario.TargetTree, cancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }
}

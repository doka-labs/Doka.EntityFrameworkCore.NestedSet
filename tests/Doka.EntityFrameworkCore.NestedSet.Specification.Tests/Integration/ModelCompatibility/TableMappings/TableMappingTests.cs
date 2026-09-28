namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies table and entity splitting against actual DDL and structural writes.</summary>
[Collection("Model compatibility")]
public abstract class TableMappingTests : ProviderTest
{
    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Shares one provider database with the other mapping-compatibility tests.</summary>
    protected TableMappingTests(
        IProviderFixture<ModelCompatibilityDatabase> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Places convention-derived access paths on the fragment containing the hierarchy columns.</summary>
    [Fact]
    public void EntitySplittingPlacesIndexesOnStructureFragment()
    {
        // Arrange
        using var context = new EntitySplitContext(ModelCompatibilityDatabase.Options<EntitySplitContext>(Engine));

        var sql = context.GetService<ISqlGenerationHelper>();

        // Act
        var script = context.Database.GenerateCreateScript();

        // Assert
        var structureIndexes = script
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(statement => statement.Contains("CREATE INDEX ", StringComparison.OrdinalIgnoreCase)
                && statement.Contains("IX_NestedSet_", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(3, structureIndexes.Length);
        Assert.All(
            structureIndexes,
            statement => Assert.Contains(
                "ON " + sql.DelimitIdentifier("EntitySplitStructure"),
                statement,
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            structureIndexes,
            statement => statement.Contains(
                "ON " + sql.DelimitIdentifier("EntitySplitPayload"),
                StringComparison.Ordinal));
    }

    /// <summary>Mutates the hierarchy principal when another entity can share its physical table row.</summary>
    [Fact]
    public async Task TableSplittingPreservesSharedRowPayloadDuringMoveAsync()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<TableSplitContext>(
            Engine,
            static options => new TableSplitContext(options));

        var hierarchy = context.NestedSet<TableSplitNode>();
        var treeId = Guid.NewGuid();
        await hierarchy.InsertRootAsync(
            new TableSplitNode
            {
                Id = 1,
                Name = "Root",
            },
            treeId,
            CancellationToken.None);

        await hierarchy.InsertChildAsync(
            new TableSplitNode
            {
                Id = 2,
                Name = "Child",
            },
            1,
            CancellationToken.None);

        await hierarchy.InsertChildAsync(
            new TableSplitNode
            {
                Id = 3,
                Name = "Target",
            },
            1,
            CancellationToken.None);

        context
            .Set<TableSplitPayload>()
            .Add(
                new TableSplitPayload
                {
                    Id = 2,
                    Description = "Kept",
                });

        await context.SaveChangesAsync(CancellationToken.None);
        var initialPayload = await context
            .Set<TableSplitPayload>()
            .AsNoTracking()
            .SingleAsync(value => value.Id == 2, CancellationToken.None);

        // Act
        await hierarchy.MoveToAsync(2, 3, CancellationToken.None);
        var nodes = await hierarchy
            .InTree(treeId)
            .Nodes
            .AsNoTracking()
            .OrderBy(node => node.Left)
            .ToArrayAsync(CancellationToken.None);

        var payload = await context
            .Set<TableSplitPayload>()
            .AsNoTracking()
            .SingleAsync(value => value.Id == 2, CancellationToken.None);

        // Assert
        Assert.Collection(
            nodes,
            node => Assert.Equal(
                (1, (int?)null, 0, 1L, 6L),
                (node.Id, node.ParentId, node.Depth, node.Left, node.Right)),
            node => Assert.Equal((3, (int?)1, 1, 2L, 5L), (node.Id, node.ParentId, node.Depth, node.Left, node.Right)),
            node => Assert.Equal((2, (int?)3, 2, 3L, 4L), (node.Id, node.ParentId, node.Depth, node.Left, node.Right)));
        Assert.Equal("Kept", initialPayload.Description);
        Assert.Equal("Kept", payload.Description);
    }

    /// <summary>Targets the sole fragment containing every structural and ordering property.</summary>
    [Fact]
    public async Task EntitySplittingSupportsCoLocatedStructureAsync()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<EntitySplitContext>(
            Engine,
            static options => new EntitySplitContext(options));

        var hierarchy = context.NestedSet<EntitySplitNode>();
        var treeId = Guid.NewGuid();
        await hierarchy.InsertRootAsync(
            new EntitySplitNode
            {
                Id = 1,
                Name = "Root",
            },
            treeId,
            CancellationToken.None);

        await hierarchy.InsertChildAsync(
            new EntitySplitNode
            {
                Id = 2,
                Name = "Child",
            },
            1,
            CancellationToken.None);

        await hierarchy.InsertChildAsync(
            new EntitySplitNode
            {
                Id = 3,
                Name = "Target",
            },
            1,
            CancellationToken.None);

        // Act
        await hierarchy.MoveToAsync(2, 3, CancellationToken.None);
        var nodes = await hierarchy
            .InTree(treeId)
            .Nodes
            .AsNoTracking()
            .OrderBy(node => node.Left)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Collection(
            nodes,
            node => Assert.Equal(("Root", (int?)null, 1L, 6L), (node.Name, node.ParentId, node.Left, node.Right)),
            node => Assert.Equal(("Target", (int?)1, 2L, 5L), (node.Name, node.ParentId, node.Left, node.Right)),
            node => Assert.Equal(("Child", (int?)3, 3L, 4L), (node.Name, node.ParentId, node.Left, node.Right)));
    }
}

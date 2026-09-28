namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies 64-bit structural coordinate persistence across every supported relational provider.</summary>
public abstract class LongCoordinateRoundTripTests : ProviderTest
{
    private readonly RelationalFixture _fixture;

    /// <summary>Creates provider cases using the shared real-database fixture.</summary>
    /// <param name="fixture">The fixture that owns each provider database.</param>
    protected LongCoordinateRoundTripTests(
        IProviderFixture<RelationalFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>
    /// Verifies boundaries and sibling positions above the 32-bit range round-trip through a 64-bit store type.
    /// </summary>
    /// <returns>A task that completes when the provider contract has been verified.</returns>
    [Fact]
    public async Task CoordinatesAboveInt32RoundTripThroughA64BitStoreType()
    {
        // Arrange
        var expectedStoreType = Engine == "Sqlite" ? "INTEGER" : "bigint";

        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var left = checked(int.MaxValue + 1L);
        var right = checked(left + 1L);
        var position = checked(right + 1L);
        var node = new TreeNode
        {
            NodeId = 1,
            TreeId = Guid.NewGuid(),
            Start = left,
            End = right,
            Tree = 1,
            Depth = 0,
            Position = position,
        };

        await context.AddAsync(node, CancellationToken.None);
        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);

        // Act
        context.ChangeTracker.Clear();
        var persisted = await context
            .Set<TreeNode>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.NodeId == node.NodeId, CancellationToken.None);

        var storeTypes = await ReadCoordinateStoreTypesAsync(context);

        // Assert
        Assert.Equal(left, persisted.Start);
        Assert.Equal(right, persisted.End);
        Assert.Equal(position, persisted.Position);
        Assert.Equal(3, storeTypes.Count);
        Assert.All(
            storeTypes,
            storeType => Assert.Equal(expectedStoreType, storeType, StringComparer.OrdinalIgnoreCase));
    }

    private static async Task<IReadOnlyList<string>> ReadCoordinateStoreTypesAsync(
        TreeContext context
    )
    {
        var entityType = context.Model.FindEntityType(typeof(TreeNode))!;
        var tableName = entityType.GetTableName()!;
        var schema = entityType.GetSchema();
        var table = StoreObjectIdentifier.Table(tableName, schema);
        var sql = context.GetService<ISqlGenerationHelper>();
        var coordinateColumns = new[]
        {
            entityType.FindProperty(nameof(TreeNode.Start))!.GetColumnName(table)!,
            entityType.FindProperty(nameof(TreeNode.End))!.GetColumnName(table)!,
            entityType.FindProperty(nameof(TreeNode.Position))!.GetColumnName(table)!,
        };

        // WHY: Reader schema comes from the created database, so the assertion cannot pass on EF metadata alone.
        var commandText = "SELECT "
            + string.Join(", ", coordinateColumns.Select(sql.DelimitIdentifier))
            + " FROM "
            + sql.DelimitIdentifier(tableName, schema)
            + " WHERE 1 = 0";

        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        command.CommandText = commandText;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SchemaOnly, CancellationToken.None);

        var columns = await reader.GetColumnSchemaAsync(CancellationToken.None);

        return columns
            .Select(column => column.DataTypeName!)
            .ToArray();
    }
}

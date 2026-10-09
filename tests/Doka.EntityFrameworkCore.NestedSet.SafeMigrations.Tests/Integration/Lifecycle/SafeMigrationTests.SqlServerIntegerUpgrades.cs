namespace Doka.EntityFrameworkCore.NestedSet.SafeMigrations.Tests;

public sealed partial class SafeMigrationTests
{
    /// <summary>Verifies the generated SQL Server upgrade widens coordinates and preserves historical rows.</summary>
    /// <param name="qualifySchema">Whether the hierarchy uses an explicit SQL Server schema.</param>
    /// <param name="populated">Whether the historical schema contains valid hierarchy rows.</param>
    /// <returns>A task that completes after verifying the full upgraded contract and Int64 capacity.</returns>
    [DatabasePlatform("SqlServer")]
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SqlServerCoordinateUpgradePreservesHierarchyAndExpandsCapacity(
        bool qualifySchema,
        bool populated
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync("SqlServer", qualifySchema);
        var baseline = database.Scaffold(MigrationStage.Baseline, "SafeBaseline");
        await database.MigrateAsync(baseline);
        if (populated)
        {
            await database.SeedAsync(MigrationStage.Baseline);
        }

        var before = await database.ReadNodesAsync(MigrationStage.Baseline);
        var upgrade = database.Scaffold(MigrationStage.Current, "SafeScopeKey", baseline);
        await using var context = database.CreateContext(MigrationStage.Current, upgrade.Assembly);
        var alterations = GetUpOperations(upgrade, context, 1)
            .OfType<SafeMigrationOperation>()
            .Where(operation => operation.Intent is AlterColumnIntent)
            .ToArray();

        var beyondInt32 = (long)int.MaxValue + 1;

        // Act
        await database.MigrateAsync(upgrade);
        var after = await database.ReadNodesAsync();
        await database.InsertNodeDirectlyAsync(new MigrationNode
        {
            NodeId = 43,
            Scope = 7,
            TreeId = 19,
            Left = beyondInt32,
            Right = beyondInt32 + 1,
            Position = beyondInt32,
            Payload = "upgraded Int64 coordinates",
        });

        var largeNode = (await database.ReadNodesAsync()).Single(node => node.NodeId == 43);
        var indexes = await database.ReadIndexesAsync();
        var checks = await database.ReadChecksAsync();
        var foreignKeys = await database.ReadForeignKeysAsync();
        var columns = await database.ReadColumnTypesAsync();
        var history = await context.Database.GetAppliedMigrationsAsync(CancellationToken.None);

        // Assert
        Assert.Equal(populated ? 2 : 0, before.Length);
        Assert.Equal(before, after);
        Assert.Equal(
            ["left_bound", "right_bound", "sibling_position"],
            alterations.Select(operation => ((AlterColumnIntent)operation.Intent).Definition.Name)
                .Order(StringComparer.Ordinal));
        AssertCurrentPhysicalContract("SqlServer", indexes, checks, foreignKeys, columns);
        Assert.Equal(
            (beyondInt32, beyondInt32 + 1, beyondInt32),
            (largeNode.Left, largeNode.Right, largeNode.Position));
        Assert.Equal(upgrade.MigrationIds, history);
    }

    /// <summary>Verifies a rejected new CHECK rolls back earlier integer and index changes in the upgrade.</summary>
    /// <param name="qualifySchema">Whether the hierarchy uses an explicit SQL Server schema.</param>
    /// <param name="checkName">The suffix identifying the single violated generated check.</param>
    /// <returns>A task that completes after proving all historical schema, rows, and history are preserved.</returns>
    [DatabasePlatform("SqlServer")]
    [Theory]
    [InlineData(false, "LeftMin")]
    [InlineData(false, "RightAfterLeft")]
    [InlineData(false, "DepthMin")]
    [InlineData(false, "PositionMin")]
    [InlineData(true, "LeftMin")]
    [InlineData(true, "RightAfterLeft")]
    [InlineData(true, "DepthMin")]
    [InlineData(true, "PositionMin")]
    public async Task SqlServerInvalidCoordinateUpgradeRollsBackSchemaAndHistory(
        bool qualifySchema,
        string checkName
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync("SqlServer", qualifySchema);
        var baseline = database.Scaffold(MigrationStage.Baseline, "SafeBaseline");
        await database.MigrateAsync(baseline);
        await database.SeedAsync(MigrationStage.Baseline);
        // WHY: Physical insertion bypasses managed writes to represent corrupt legacy data before checks existed.
        await database.InsertNodeDirectlyAsync(CreateInvalidSqlServerNode(checkName));
        var upgrade = database.Scaffold(MigrationStage.Current, "SafeScopeKey", baseline);
        await using var context = database.CreateContext(MigrationStage.Current, upgrade.Assembly);
        var indexesBefore = IndexSignatures(await database.ReadIndexesAsync());
        var checksBefore = await database.ReadChecksAsync();
        var nodesBefore = await database.ReadNodesAsync(MigrationStage.Baseline);
        var columnsBefore = await database.ReadColumnTypesAsync();
        var foreignKeysBefore = ForeignKeySignatures(await database.ReadForeignKeysAsync());
        var historyBefore = (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();

        // Act
        var exception = await Record.ExceptionAsync(() => database.MigrateAsync(upgrade));

        // Assert
        var databaseException = Assert.IsAssignableFrom<DbException>(exception);
        Assert.Contains("doka_sm_data_blocked", databaseException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(indexesBefore, IndexSignatures(await database.ReadIndexesAsync()));
        Assert.Equal(checksBefore, await database.ReadChecksAsync());
        Assert.Equal(nodesBefore, await database.ReadNodesAsync(MigrationStage.Baseline));
        Assert.Equal(columnsBefore, await database.ReadColumnTypesAsync());
        Assert.Equal(foreignKeysBefore, ForeignKeySignatures(await database.ReadForeignKeysAsync()));
        Assert.Equal([baseline.MigrationIds[0]], historyBefore);
        Assert.Equal(historyBefore, await context.Database.GetAppliedMigrationsAsync(CancellationToken.None));
    }

    /// <summary>Verifies widening approval does not permit a generated Int64-to-Int32 narrowing migration.</summary>
    /// <param name="qualifySchema">Whether the hierarchy uses an explicit SQL Server schema.</param>
    /// <param name="beyondInt32">Whether a row exceeds Int32, rather than all rows fitting the narrower type.</param>
    /// <returns>A task that completes after proving Int64 rows and the current schema survive rejection.</returns>
    [DatabasePlatform("SqlServer")]
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SqlServerCoordinateNarrowingPreservesSchemaAndData(
        bool qualifySchema,
        bool beyondInt32
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync("SqlServer", qualifySchema);
        var current = database.Scaffold(MigrationStage.Current, "SafeInitial");
        await database.MigrateAsync(current);
        await database.SeedAsync();
        var coordinate = beyondInt32 ? (long)int.MaxValue + 1 : 5;
        await database.InsertNodeDirectlyAsync(new MigrationNode
        {
            NodeId = 43,
            Scope = 7,
            TreeId = 19,
            Left = coordinate,
            Right = coordinate + 1,
            Position = coordinate,
            Payload = "must retain Int64 capacity",
        });

        // WHY: Scaffold a real reverse model transition; do not replace generated narrowing with synthetic SQL.
        var narrowing = database.Scaffold(MigrationStage.Baseline, "RejectedNarrowing", current);
        await using var context = database.CreateContext(MigrationStage.Baseline, narrowing.Assembly);
        var indexesBefore = IndexSignatures(await database.ReadIndexesAsync());
        var checksBefore = await database.ReadChecksAsync();
        var nodesBefore = await database.ReadNodesAsync();
        var columnsBefore = await database.ReadColumnTypesAsync();
        var foreignKeysBefore = ForeignKeySignatures(await database.ReadForeignKeysAsync());
        var historyBefore = (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();

        // Act
        var exception = await Record.ExceptionAsync(() => database.MigrateAsync(narrowing));

        // Assert
        var databaseException = Assert.IsAssignableFrom<DbException>(exception);
        Assert.Contains("doka_sm_different", databaseException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(indexesBefore, IndexSignatures(await database.ReadIndexesAsync()));
        Assert.Equal(checksBefore, await database.ReadChecksAsync());
        Assert.Equal(nodesBefore, await database.ReadNodesAsync());
        Assert.Equal(columnsBefore, await database.ReadColumnTypesAsync());
        Assert.Equal(foreignKeysBefore, ForeignKeySignatures(await database.ReadForeignKeysAsync()));
        Assert.Equal(current.MigrationIds, historyBefore);
        Assert.Equal(historyBefore, await context.Database.GetAppliedMigrationsAsync(CancellationToken.None));
    }

    /// <summary>Captures foreign-key names, ordered column pairs, and delete actions for rollback comparison.</summary>
    private static string[] ForeignKeySignatures(
        MigrationForeignKey[] foreignKeys
    ) => foreignKeys
        // WHY: Catalog rereads allocate new column lists; their record equality would compare list identities.
        .Select(key => key.Name + ":" + string.Join(",", key.DependentColumns)
            + ":" + string.Join(",", key.PrincipalColumns) + ":" + key.DeleteAction)
        .Order(StringComparer.Ordinal)
        .ToArray();
}

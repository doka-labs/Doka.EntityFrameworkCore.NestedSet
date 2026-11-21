namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

public sealed partial class AncestorIndexMigrationTests
{

    /// <summary>Removes only the optional schema on Down and retains the original populated hierarchy.</summary>
    /// <param name="engine">The real relational engine.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task MigrateAsync_Down_PreservesDataAndOriginalIndexes(
        string engine
    )
    {
        // Arrange
        await using var database = await AncestorIndexDatabase.CreateAsync(
            _fixture,
            engine,
            cancellationToken: TestContext.Current.CancellationToken,
            configureOptions: null,
            configureDesignServices: null);

        var initial = database.Scaffold(false, "AOrdinaryInitial");
        await database.MigrateAsync(
            initial,
            cancellationToken: TestContext.Current.CancellationToken,
            enabled: false,
            target: null,
            adoptIndex: false,
            enableIntegration: true,
            splitStructure: false);
        await database.PhysicalDatabase.SeedAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Current);
        var before = await database.PhysicalDatabase.ReadNodesAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Current);

        var indexesBefore = Signatures(
            await database.PhysicalDatabase.ReadIndexesAsync(
                cancellationToken: TestContext.Current.CancellationToken));

        var upgrade = database.Scaffold(true, "BAddAncestorIndex", initial);
        await database.MigrateAsync(
            upgrade,
            cancellationToken: TestContext.Current.CancellationToken,
            enabled: true,
            target: null,
            adoptIndex: false,
            enableIntegration: true,
            splitStructure: false);

        // Act
        await database.MigrateAsync(
            upgrade,
            target: initial.MigrationIds[0],
            cancellationToken: TestContext.Current.CancellationToken,
            enabled: true,
            adoptIndex: false,
            enableIntegration: true,
            splitStructure: false);

        var after = await database.PhysicalDatabase.ReadNodesAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Current);

        var indexesAfter = Signatures(
            await database.PhysicalDatabase.ReadIndexesAsync(
                cancellationToken: TestContext.Current.CancellationToken));

        await using var ordinary = database.CreateContext(false);
        var sql = ordinary.GetService<ISqlGenerationHelper>();

        // WHY: DDL identifiers cannot be parameters; every identifier comes from fixed test metadata and
        // is delimited by the active provider, including the alias qualification required by SQLite.
        var probe = "SELECT entry."
            + sql.DelimitIdentifier(AncestorIndexContext.FlagColumn)
            + " AS "
            + sql.DelimitIdentifier("Value")
            + " FROM "
            + sql.DelimitIdentifier(MigrationContext.TableName, database.PhysicalDatabase.Schema)
            + " entry";

        var missingColumn = await Record.ExceptionAsync(async () =>
        {
            await ordinary
                .Database
                .SqlQueryRaw<int>(probe)
                .ToArrayAsync(CancellationToken.None);
        });

        // Assert
        Assert.Equal(before, after);
        Assert.Equal(indexesBefore, indexesAfter);
        Assert.IsType<DbException>(missingColumn, exactMatch: false);
    }

    /// <summary>Reapplies the optional schema after Down without changing the original populated hierarchy.</summary>
    /// <param name="engine">The real relational engine.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task MigrateAsync_Reapply_RestoresComputedValuesAndOneAncestorIndex(
        string engine
    )
    {
        // Arrange
        await using var database = await AncestorIndexDatabase.CreateAsync(
            _fixture,
            engine,
            cancellationToken: TestContext.Current.CancellationToken,
            configureOptions: null,
            configureDesignServices: null);

        var initial = database.Scaffold(false, "AOrdinaryInitial");
        await database.MigrateAsync(
            initial,
            cancellationToken: TestContext.Current.CancellationToken,
            enabled: false,
            target: null,
            adoptIndex: false,
            enableIntegration: true,
            splitStructure: false);
        await database.PhysicalDatabase.SeedAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Current);
        var before = await database.PhysicalDatabase.ReadNodesAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Current);

        var upgrade = database.Scaffold(true, "BAddAncestorIndex", initial);
        await database.MigrateAsync(
            upgrade,
            cancellationToken: TestContext.Current.CancellationToken,
            enabled: true,
            target: null,
            adoptIndex: false,
            enableIntegration: true,
            splitStructure: false);
        await database.MigrateAsync(
            upgrade,
            target: initial.MigrationIds[0],
            cancellationToken: TestContext.Current.CancellationToken,
            enabled: true,
            adoptIndex: false,
            enableIntegration: true,
            splitStructure: false);

        // Act
        await database.MigrateAsync(
            upgrade,
            cancellationToken: TestContext.Current.CancellationToken,
            enabled: true,
            target: null,
            adoptIndex: false,
            enableIntegration: true,
            splitStructure: false);
        var after = await database.PhysicalDatabase.ReadNodesAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Current);

        var indexes = await database.PhysicalDatabase.ReadIndexesAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await using var context = database.CreateContext(assembly: upgrade.Assembly);
        var flags = await ReadFlagsAsync(context, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(before, after);
        Assert.Equal([true, false], flags);
        AssertAncestorIndex(indexes, engine != "PostgreSql");
    }
}

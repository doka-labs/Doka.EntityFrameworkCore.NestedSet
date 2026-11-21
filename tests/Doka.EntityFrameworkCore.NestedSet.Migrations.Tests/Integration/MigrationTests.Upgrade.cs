namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

public sealed partial class MigrationTests
{
    /// <summary>Upgrades tree identity and coordinate widths without changing persisted application values.</summary>
    /// <param name="engine">The provider and real database engine to exercise.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task MigrateAsync_Upgrade_PreservesEveryPersistedValue(
        string engine
    )
    {
        // Arrange
        await using var database = await _fixture.CreateDatabaseAsync(
            engine,
            cancellationToken: TestContext.Current.CancellationToken,
            configureOptions: null,
            configureDesignServices: null,
            qualifySchema: true);

        var baseline = database.Scaffold(MigrationStage.Baseline, "Baseline");
        await database.MigrateAsync(baseline, cancellationToken: TestContext.Current.CancellationToken, target: null);
        await database.SeedAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Baseline);
        var before = await database.ReadNodesAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Baseline);

        var upgrade = database.Scaffold(MigrationStage.Current, "TreeIdentityAndInt64", baseline);

        // Act
        await database.MigrateAsync(upgrade, cancellationToken: TestContext.Current.CancellationToken, target: null);
        var after = await database.ReadNodesAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Current);
        var indexes = await database.ReadIndexesAsync(cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext(MigrationStage.Current, upgrade.Assembly);
        var operations = upgrade.GetMigration(context, 1)
            .UpOperations;

        // Assert
        Assert.Equal(before, after);
        Assert.DoesNotContain(operations, operation => operation is AddColumnOperation { Name: "tree_id" });

        if (engine != "Sqlite")
        {
            Assert.Contains(operations, operation => operation is AlterColumnOperation { Name: "left_bound" });
            Assert.Contains(operations, operation => operation is AlterColumnOperation { Name: "right_bound" });
            Assert.Contains(operations, operation => operation is AlterColumnOperation { Name: "sibling_position" });
        }

        Assert.Contains(
            operations.OfType<CreateIndexOperation>(),
            operation => operation.Columns.SequenceEqual(["tree_scope", "tree_id", "left_bound"]));

        Assert.DoesNotContain(
            operations,
            operation => operation is DropTableOperation { Name: MigrationContext.TableName });

        AssertCurrentIndexes(engine, indexes);
    }

    /// <summary>Restores the complete historical schema and data when rolling back the upgrade.</summary>
    /// <param name="engine">The provider and real database engine to exercise.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task MigrateAsync_Down_PreservesTheOriginalIndexesAndData(
        string engine
    )
    {
        // Arrange
        await using var database = await _fixture.CreateDatabaseAsync(
            engine,
            cancellationToken: TestContext.Current.CancellationToken,
            configureOptions: null,
            configureDesignServices: null,
            qualifySchema: true);

        var baseline = database.Scaffold(MigrationStage.Baseline, "Baseline");
        await database.MigrateAsync(baseline, cancellationToken: TestContext.Current.CancellationToken, target: null);
        await database.SeedAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Baseline);
        var originalIndexes = await database.ReadIndexesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var before = await database.ReadNodesAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Baseline);

        var upgrade = database.Scaffold(MigrationStage.Current, "TreeIdentityAndInt64", baseline);
        await database.MigrateAsync(upgrade, cancellationToken: TestContext.Current.CancellationToken, target: null);

        // Act
        await database.MigrateAsync(
            upgrade,
            cancellationToken: TestContext.Current.CancellationToken,
            target: baseline.MigrationIds[0]);

        var indexes = await database.ReadIndexesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var after = await database.ReadNodesAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Baseline);

        // Assert
        Assert.Equal(before, after);
        Assert.Equal(IndexSignatures(originalIndexes), IndexSignatures(indexes));
        Assert.Equal(5, indexes.Length);
    }

    /// <summary>Reapplies a previously rolled-back tree-contract upgrade without duplicating metadata.</summary>
    /// <param name="engine">The provider and real database engine to exercise.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task MigrateAsync_Reapply_RestoresTheCurrentTreeContract(
        string engine
    )
    {
        // Arrange
        await using var database = await _fixture.CreateDatabaseAsync(
            engine,
            cancellationToken: TestContext.Current.CancellationToken,
            configureOptions: null,
            configureDesignServices: null,
            qualifySchema: true);

        var baseline = database.Scaffold(MigrationStage.Baseline, "Baseline");
        var upgrade = database.Scaffold(MigrationStage.Current, "TreeIdentityAndInt64", baseline);
        await database.MigrateAsync(baseline, cancellationToken: TestContext.Current.CancellationToken, target: null);
        await database.SeedAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Baseline);
        await database.MigrateAsync(upgrade, cancellationToken: TestContext.Current.CancellationToken, target: null);
        var before = await database.ReadNodesAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Current);
        await database.MigrateAsync(
            upgrade,
            cancellationToken: TestContext.Current.CancellationToken,
            target: baseline.MigrationIds[0]);

        // Act
        await database.MigrateAsync(upgrade, cancellationToken: TestContext.Current.CancellationToken, target: null);
        var indexes = await database.ReadIndexesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var after = await database.ReadNodesAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Current);

        // Assert
        Assert.Equal(before, after);
        AssertCurrentIndexes(engine, indexes);
    }

    /// <summary>Rejects a conflicting ordinary index definition instead of treating presence as correctness.</summary>
    /// <param name="engine">The provider and real database engine to exercise.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task MigrateAsync_ConflictingIndex_CausesOrdinaryUpgradeToFailWithoutDataLoss(
        string engine
    )
    {
        // Arrange
        await using var database = await _fixture.CreateDatabaseAsync(
            engine,
            cancellationToken: TestContext.Current.CancellationToken,
            configureOptions: null,
            configureDesignServices: null,
            qualifySchema: true);

        var baseline = database.Scaffold(MigrationStage.Baseline, "Baseline");
        await database.MigrateAsync(baseline, cancellationToken: TestContext.Current.CancellationToken, target: null);
        await database.SeedAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Baseline);
        var before = await database.ReadNodesAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Baseline);

        var upgrade = database.Scaffold(MigrationStage.Current, "TreeIdentityAndInt64", baseline);
        await using var context = database.CreateContext(MigrationStage.Current, upgrade.Assembly);
        var index = upgrade
            .GetMigration(context, 1)
            .UpOperations
            .OfType<CreateIndexOperation>()
            .Single(operation => operation.Columns.SequenceEqual(["tree_scope", "tree_id", "left_bound"]));

        var sql = context.GetService<ISqlGenerationHelper>();
        var conflicting = "CREATE INDEX "
            + sql.DelimitIdentifier(index.Name)
            + " ON "
            + sql.DelimitIdentifier(index.Table, index.Schema)
            + " ("
            + sql.DelimitIdentifier("entry_depth")
            + ")";

        await context.Database.ExecuteSqlRawAsync(conflicting, CancellationToken.None);

        // Act
        var exception = await Record.ExceptionAsync(() => database.MigrateAsync(
            upgrade,
            cancellationToken: TestContext.Current.CancellationToken,
            target: null));

        var after = await database.ReadNodesAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Baseline);

        var applied = await context.Database.GetAppliedMigrationsAsync(CancellationToken.None);

        // Assert
        Assert.IsType<DbException>(exception, exactMatch: false);
        Assert.Equal(before, after);
        Assert.Equal(baseline.MigrationIds, applied);
    }
}

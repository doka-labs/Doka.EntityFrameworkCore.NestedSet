namespace Doka.EntityFrameworkCore.NestedSet.SafeMigrations.Tests;

public sealed partial class SafeMigrationTests
{
    /// <summary>Verifies fresh databases receive every index through generated safe migration operations.</summary>
    /// <param name="engine">The relational database engine.</param>
    /// <param name="qualifySchema">Whether the model preserves an explicit schema or database qualifier.</param>
    /// <returns>A task that completes after applying and inspecting the generated migration.</returns>
    [Theory]
    [InlineData("Sqlite", false)]
    [InlineData("PostgreSql", true)]
    [InlineData("MySql", false)]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", false)]
    [InlineData("MariaDb", true)]
    public async Task FreshMigrationCreatesEveryIndex(
        string engine,
        bool qualifySchema
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync(engine, qualifySchema);
        var chain = database.Scaffold(MigrationStage.Current, "SafeInitial");
        await using var context = database.CreateContext(MigrationStage.Current, chain.Assembly);
        var hierarchy = context
            .NestedSet<MigrationNode>()
            .ForScope(7);

        var operations = GetIndexOperations(chain, context);
        var beyondInt32 = (long)int.MaxValue + 1;

        // Act
        await database.MigrateAsync(chain);
        await hierarchy.InsertRootAsync(
            new MigrationNode
            {
                NodeId = 41,
                Payload = "parent payload",
            },
            treeId: 0,
            CancellationToken.None);

        await hierarchy.InsertChildAsync(
            new MigrationNode
            {
                NodeId = 42,
                Payload = "child payload",
            },
            41,
            cancellationToken: CancellationToken.None);

        var validationReport = await hierarchy
            .InTree(0)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        var descendants = await hierarchy
            .DescendantsOf(41)
            .Select(node => node.NodeId)
            .ToArrayAsync(CancellationToken.None);

        await database.InsertNodeDirectlyAsync(
            new MigrationNode
            {
                NodeId = 43,
                Scope = 7,
                TreeId = 19,
                Left = beyondInt32,
                Right = beyondInt32 + 1,
                Position = beyondInt32,
                Payload = "large coordinates",
            });

        var largeNode = (await database.ReadNodesAsync()).Single(node => node.NodeId == 43);

        // WHY: Index analysis needs the containing table; analyze after the complete migration has created it.
        var report = await context
            .GetService<ISafeMigrationRunner>()
            .AnalyzeAsync(
                context,
                operations,
                new SafeMigrationRunOptions("nested-set-fresh-index-contract"),
                CancellationToken.None);

        var indexes = await database.ReadIndexesAsync();
        var checks = await database.ReadChecksAsync();
        var foreignKeys = await database.ReadForeignKeysAsync();
        var columnTypes = await database.ReadColumnTypesAsync();

        // Assert
        Assert.True(validationReport.IsValid);
        Assert.Equal([42], descendants);
        Assert.Equal(
            (beyondInt32, beyondInt32 + 1, beyondInt32),
            (largeNode.Left, largeNode.Right, largeNode.Position));
        AssertFreshIndexOperations(operations);
        Assert.All(
            operations,
            operation => Assert.Equal(database.Schema, ((EnsureIndexIntent)operation.Intent).Definition.Schema));
        Assert.Contains(
            "CreateCompositeIndexIfNotExistsFromModel",
            chain.LatestMigrationCode,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            chain.GetMigration(context, 0)
                .UpOperations,
            operation => operation is CreateIndexOperation);
        Assert.Equal(SafeMigrationReportStatus.Ready, report.Status);
        Assert.Equal(operations.Length, report.Assessments.Count);
        Assert.All(
            report.Assessments,
            assessment =>
            {
                Assert.Equal(SafeMigrationObservedState.Matching, assessment.ObservedState);
                Assert.Equal(SafeMigrationAction.NoOp, assessment.Action);
            });
        AssertCurrentPhysicalContract(engine, indexes, checks, foreignKeys, columnTypes);
    }

    /// <summary>Verifies SQLite applies the direct generated upgrade without changing hierarchy data.</summary>
    /// <returns>A task that completes after upgrading a populated historical model.</returns>
    [Fact]
    public async Task SqliteUpgradePreservesPopulatedHierarchy()
    {
        // Arrange
        await using var database = await CreateDatabaseAsync("Sqlite", false);
        var baseline = database.Scaffold(MigrationStage.Baseline, "SafeBaseline");
        await database.MigrateAsync(baseline);
        await database.SeedAsync(MigrationStage.Baseline);
        var before = await database.ReadNodesAsync(MigrationStage.Baseline);
        var upgrade = database.Scaffold(MigrationStage.Current, "SafeScopeKey", baseline);
        await using var context = database.CreateContext(MigrationStage.Current, upgrade.Assembly);
        var operations = GetIndexOperations(upgrade, context, 1);

        // Act
        await database.MigrateAsync(upgrade);
        var after = await database.ReadNodesAsync();
        var indexes = await database.ReadIndexesAsync();

        // Assert
        AssertUpgradeIndexOperations(operations);
        Assert.All(
            operations,
            operation => Assert.Equal(database.Schema, ((EnsureIndexIntent)operation.Intent).Definition.Schema));
        Assert.Equal(before, after);
        AssertCurrentIndexes(indexes);
    }

    /// <summary>Verifies server preflight rejects opaque generated checks before any upgrade mutation.</summary>
    /// <param name="engine">The server database engine.</param>
    /// <param name="qualifySchema">Whether the model preserves an explicit schema or database qualifier.</param>
    /// <returns>A task that completes after comparing data, catalog, and migration history.</returns>
    [Theory]
    [InlineData("PostgreSql", true)]
    [InlineData("MySql", false)]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", false)]
    [InlineData("MariaDb", true)]
    public async Task ServerUpgradePreflightRejectsOpaqueChecksWithoutMutation(
        string engine,
        bool qualifySchema
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync(engine, qualifySchema);
        var baseline = database.Scaffold(MigrationStage.Baseline, "SafeBaseline");
        await database.MigrateAsync(baseline);
        await database.SeedAsync(MigrationStage.Baseline);
        var upgrade = database.Scaffold(MigrationStage.Current, "SafeScopeKey", baseline);
        await using var context = database.CreateContext(MigrationStage.Current, upgrade.Assembly);
        var operations = GetUpOperations(upgrade, context, 1);
        var indexOperations = GetIndexOperations(upgrade, context, 1);
        var indexesBefore = IndexSignatures(await database.ReadIndexesAsync());
        var nodesBefore = await database.ReadNodesAsync(MigrationStage.Baseline);
        var historyBefore = (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();

        // Act
        var report = await context
            .GetService<ISafeMigrationRunner>()
            .AnalyzePendingMigrationsAsync(
                context,
                new SafeMigrationRunOptions("nested-set-server-upgrade"),
                CancellationToken.None);

        var indexesAfter = IndexSignatures(await database.ReadIndexesAsync());
        var nodesAfter = await database.ReadNodesAsync(MigrationStage.Baseline);
        var historyAfter = await context.Database.GetAppliedMigrationsAsync(CancellationToken.None);

        // Assert
        AssertUpgradeIndexOperations(indexOperations);
        Assert.All(
            indexOperations,
            operation => Assert.Equal(database.Schema, ((EnsureIndexIntent)operation.Intent).Definition.Schema));
        Assert.Equal(operations.Length, report.Assessments.Count);
        AssertOpaqueCheckConstraintBlockers(report);
        Assert.Equal(indexesBefore, indexesAfter);
        Assert.Equal(nodesBefore, nodesAfter);
        Assert.Equal([baseline.MigrationIds[0]], historyBefore);
        Assert.Equal(historyBefore, historyAfter);
    }

    /// <summary>Verifies SQLite generated Down preserves data and earlier indexes.</summary>
    /// <returns>A task that completes after rolling back the generated index upgrade.</returns>
    [Fact]
    public async Task SqliteDowngradePreservesEarlierIndexesAndData()
    {
        // Arrange
        await using var database = await CreateDatabaseAsync("Sqlite", false);
        var baseline = database.Scaffold(MigrationStage.Baseline, "SafeBaseline");
        await database.MigrateAsync(baseline);
        var originalIndexes = IndexSignatures(await database.ReadIndexesAsync());
        var upgrade = database.Scaffold(MigrationStage.Current, "SafeScopeKey", baseline);
        await database.MigrateAsync(upgrade);
        await database.SeedAsync();
        var before = await database.ReadNodesAsync();

        // Act
        await database.MigrateAsync(upgrade, baseline.MigrationIds[0]);
        var indexes = await database.ReadIndexesAsync();
        var after = await database.ReadNodesAsync();

        // Assert
        AssertBaselineIndexes(indexes);
        Assert.Equal(originalIndexes, IndexSignatures(indexes));
        Assert.Equal(before, after);
    }

    /// <summary>Verifies SQLite can reapply an index upgrade after its generated Down migration.</summary>
    /// <returns>A task that completes after reapplying the index upgrade to the populated schema.</returns>
    [Fact]
    public async Task SqliteReapplyRestoresIndexAndPreservesData()
    {
        // Arrange
        await using var database = await CreateDatabaseAsync("Sqlite", false);
        var baseline = database.Scaffold(MigrationStage.Baseline, "SafeBaseline");
        var upgrade = database.Scaffold(MigrationStage.Current, "SafeScopeKey", baseline);
        await database.MigrateAsync(upgrade);
        await database.SeedAsync();
        await database.MigrateAsync(upgrade, baseline.MigrationIds[0]);
        var before = await database.ReadNodesAsync();

        // Act
        await database.MigrateAsync(upgrade);
        var indexes = await database.ReadIndexesAsync();
        var after = await database.ReadNodesAsync();

        // Assert
        AssertCurrentIndexes(indexes);
        Assert.Equal(before, after);
    }
}

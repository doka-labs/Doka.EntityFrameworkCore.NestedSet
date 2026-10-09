namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>Verifies generated migrations independently of the optional SafeMigrations integration.</summary>
[DatabasePlatform]
public sealed class MigrationTests : IClassFixture<MigrationFixture>
{
    private readonly MigrationFixture _fixture;

    /// <summary>Creates migration scenarios with reusable engines and isolated databases.</summary>
    /// <param name="fixture">The engine owner supplied by xUnit.</param>
    public MigrationTests(
        MigrationFixture fixture
    )
    {
        _fixture = fixture;
    }

    /// <summary>Creates every structural index through compiled ordinary EF migrations.</summary>
    /// <param name="engine">The provider and real database engine to exercise.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task FreshMigrationCreatesOrderedIndexesWithCustomRelationalNames(
        string engine
    )
    {
        // Arrange
        await using var database = await _fixture.CreateDatabaseAsync(engine);
        var chain = database.Scaffold(MigrationStage.Current, "Initial");

        // Act
        await database.MigrateAsync(chain);
        var indexes = await database.ReadIndexesAsync();
        await using var context = database.CreateContext(MigrationStage.Current, chain.Assembly);
        var migration = chain.GetMigration(context, 0);

        // Assert
        AssertCurrentIndexes(indexes);
        Assert.Equal(
            6,
            migration
                .UpOperations
                .OfType<CreateIndexOperation>()
                .Count());
        Assert.DoesNotContain("SafeMigrations", chain.LatestMigrationCode, StringComparison.Ordinal);
        Assert.All(indexes, x => Assert.True(x.Name.Length <= context.Model.GetMaxIdentifierLength()));
        Assert.Contains(indexes, x => x.Name == "application_payload_lookup");
    }

    /// <summary>Upgrades tree identity and coordinate widths without changing persisted application values.</summary>
    /// <param name="engine">The provider and real database engine to exercise.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task UpgradePreservesEveryPersistedValue(
        string engine
    )
    {
        // Arrange
        await using var database = await _fixture.CreateDatabaseAsync(engine);
        var baseline = database.Scaffold(MigrationStage.Baseline, "Baseline");
        await database.MigrateAsync(baseline);
        await database.SeedAsync(MigrationStage.Baseline);
        var before = await database.ReadNodesAsync(MigrationStage.Baseline);
        var upgrade = database.Scaffold(MigrationStage.Current, "TreeIdentityAndInt64", baseline);

        // Act
        await database.MigrateAsync(upgrade);
        var after = await database.ReadNodesAsync();
        var indexes = await database.ReadIndexesAsync();
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
        AssertCurrentIndexes(indexes);
    }

    /// <summary>Restores the complete historical schema and data when rolling back the upgrade.</summary>
    /// <param name="engine">The provider and real database engine to exercise.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task DownPreservesTheOriginalIndexesAndData(
        string engine
    )
    {
        // Arrange
        await using var database = await _fixture.CreateDatabaseAsync(engine);
        var baseline = database.Scaffold(MigrationStage.Baseline, "Baseline");
        await database.MigrateAsync(baseline);
        await database.SeedAsync(MigrationStage.Baseline);
        var originalIndexes = await database.ReadIndexesAsync();
        var before = await database.ReadNodesAsync(MigrationStage.Baseline);
        var upgrade = database.Scaffold(MigrationStage.Current, "TreeIdentityAndInt64", baseline);
        await database.MigrateAsync(upgrade);

        // Act
        await database.MigrateAsync(upgrade, baseline.MigrationIds[0]);
        var indexes = await database.ReadIndexesAsync();
        var after = await database.ReadNodesAsync(MigrationStage.Baseline);

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
    public async Task ReapplyRestoresTheCurrentTreeContract(
        string engine
    )
    {
        // Arrange
        await using var database = await _fixture.CreateDatabaseAsync(engine);
        var baseline = database.Scaffold(MigrationStage.Baseline, "Baseline");
        var upgrade = database.Scaffold(MigrationStage.Current, "TreeIdentityAndInt64", baseline);
        await database.MigrateAsync(baseline);
        await database.SeedAsync(MigrationStage.Baseline);
        await database.MigrateAsync(upgrade);
        var before = await database.ReadNodesAsync();
        await database.MigrateAsync(upgrade, baseline.MigrationIds[0]);

        // Act
        await database.MigrateAsync(upgrade);
        var indexes = await database.ReadIndexesAsync();
        var after = await database.ReadNodesAsync();

        // Assert
        Assert.Equal(before, after);
        AssertCurrentIndexes(indexes);
    }

    /// <summary>Round-trips the generated snapshot without producing a phantom index migration.</summary>
    /// <param name="engine">The provider and real database engine to exercise.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task SubsequentScaffoldHasNoModelChanges(
        string engine
    )
    {
        // Arrange
        await using var database = await _fixture.CreateDatabaseAsync(engine);
        var initial = database.Scaffold(MigrationStage.Current, "Initial");
        await database.MigrateAsync(initial);

        // Act
        var next = database.Scaffold(MigrationStage.Current, "Unchanged", initial);
        await using var context = database.CreateContext(MigrationStage.Current, next.Assembly);
        var migration = next.GetMigration(context, 1);

        // Assert
        Assert.Empty(migration.UpOperations);
        Assert.Empty(migration.DownOperations);
        Assert.False(
            context
                .GetService<IMigrator>()
                .HasPendingModelChanges());
    }

    /// <summary>Preserves specialized application indexes and round-trips their ordinary structural fallback.</summary>
    /// <param name="engine">The provider supporting filtered application indexes.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task SpecializedIndexAndFallbackSurviveSnapshotRoundTrip(
        string engine
    )
    {
        // Arrange
        await using var database = await _fixture.CreateDatabaseAsync(engine);
        var initial = database.Scaffold(MigrationStage.Specialized, "Specialized");
        await database.MigrateAsync(initial);

        // Act
        var next = database.Scaffold(MigrationStage.Specialized, "Unchanged", initial);
        await using var context = database.CreateContext(MigrationStage.Specialized, next.Assembly);
        var migration = next.GetMigration(context, 1);
        var indexes = await database.ReadIndexesAsync();

        // Assert
        var specialized = Assert.Single(indexes, x => x.Name == "application_filtered_left_lookup");
        Assert.Equal(["tree_scope", "tree_id", "left_bound"], specialized.Columns);
        Assert.False(specialized.IsOrdinary);
        Assert.Single(indexes, x => x.IsOrdinary && x.Columns.SequenceEqual(specialized.Columns));
        Assert.Equal(8, indexes.Length);
        Assert.Empty(migration.UpOperations);
        Assert.Empty(migration.DownOperations);
        Assert.False(
            context
                .GetService<IMigrator>()
                .HasPendingModelChanges());
    }

    /// <summary>Exercises the lock table and hierarchy writes created by an actual migration.</summary>
    /// <param name="engine">The provider and real database engine to exercise.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task MigratedSchemaSupportsNestedSetMutations(
        string engine
    )
    {
        // Arrange
        await using var database = await _fixture.CreateDatabaseAsync(engine);
        var chain = database.Scaffold(MigrationStage.Current, "Initial");
        await database.MigrateAsync(chain);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<MigrationNode>()
            .ForScope(7);

        // Act
        await hierarchy.InsertRootAsync(
            new MigrationNode
            {
                NodeId = 41,
                Payload = "root",
            },
            treeId: 0,
            CancellationToken.None);
        await hierarchy.InsertChildAsync(
            new MigrationNode
            {
                NodeId = 42,
                Payload = "child",
            },
            41,
            cancellationToken: CancellationToken.None);
        var report = await hierarchy
            .InTree(0)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);
        var nodes = await database.ReadNodesAsync();

        // Assert
        Assert.True(report.IsValid);
        Assert.Equal(
            [
                new MigrationNodeValue(
                    41,
                    7,
                    0,
                    null,
                    1,
                    4,
                    0,
                    0,
                    "root",
                    0),
                new MigrationNodeValue(
                    42,
                    7,
                    0,
                    41,
                    2,
                    3,
                    1,
                    0,
                    "child",
                    0),
            ],
            nodes);
    }

    /// <summary>Rejects a conflicting ordinary index definition instead of treating presence as correctness.</summary>
    /// <param name="engine">The provider and real database engine to exercise.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task ConflictingIndexCausesOrdinaryUpgradeToFailWithoutDataLoss(
        string engine
    )
    {
        // Arrange
        await using var database = await _fixture.CreateDatabaseAsync(engine);
        var baseline = database.Scaffold(MigrationStage.Baseline, "Baseline");
        await database.MigrateAsync(baseline);
        await database.SeedAsync(MigrationStage.Baseline);
        var before = await database.ReadNodesAsync(MigrationStage.Baseline);
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
        var exception = await Record.ExceptionAsync(() => database.MigrateAsync(upgrade));
        var after = await database.ReadNodesAsync(MigrationStage.Baseline);
        var applied = await context.Database.GetAppliedMigrationsAsync(CancellationToken.None);

        // Assert
        Assert.IsType<DbException>(exception, exactMatch: false);
        Assert.Equal(before, after);
        Assert.Equal(baseline.MigrationIds, applied);
    }

    /// <summary>Qualifies Int64 storage, checks, self-FK metadata, and mixed ordering on a migrated database.</summary>
    /// <param name="engine">The provider and real database engine to exercise.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task FreshMigrationCreatesTheCompletePhysicalTreeContract(
        string engine
    )
    {
        // Arrange
        await using var database = await _fixture.CreateDatabaseAsync(engine);
        var chain = database.Scaffold(MigrationStage.Current, "PhysicalContract");
        await database.MigrateAsync(chain);
        var beyondInt32 = (long)int.MaxValue + 1;
        var node = new MigrationNode
        {
            NodeId = 51,
            Scope = 7,
            TreeId = 19,
            Left = beyondInt32,
            Right = beyondInt32 + 1,
            Position = beyondInt32,
            Payload = "large",
            Category = 3,
        };

        // Act
        await database.InsertNodeDirectlyAsync(node);
        var nodes = await database.ReadNodesAsync();
        var indexes = await database.ReadIndexesAsync();
        var checks = await database.ReadChecksAsync();
        var foreignKeys = await database.ReadForeignKeysAsync();
        var columnTypes = await database.ReadColumnTypesAsync();

        // Assert
        var stored = Assert.Single(nodes);
        Assert.Equal((beyondInt32, beyondInt32 + 1, beyondInt32), (stored.Left, stored.Right, stored.Position));
        AssertPhysicalContract(engine, indexes, checks, foreignKeys, columnTypes);
    }

    /// <summary>Proves the generated database checks reject an invalid left boundary.</summary>
    /// <param name="engine">The provider and real database engine to exercise.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task GeneratedChecksRejectInvalidLeftBoundary(
        string engine
    )
    {
        // Arrange
        await using var database = await _fixture.CreateDatabaseAsync(engine);
        var chain = database.Scaffold(MigrationStage.Current, "CheckEnforcement");
        await database.MigrateAsync(chain);
        var invalid = new MigrationNode
        {
            NodeId = 61,
            Scope = 7,
            TreeId = 23,
            Left = 0,
            Right = 1,
            Payload = "invalid",
        };

        // Act
        var exception = await Record.ExceptionAsync(() => database.InsertNodeDirectlyAsync(invalid));
        var nodes = await database.ReadNodesAsync();

        // Assert
        Assert.IsType<DbException>(exception, exactMatch: false);
        Assert.Empty(nodes);
    }

    /// <summary>Checks that ordinary migration validation does not acquire optional integration dependencies.</summary>
    [Fact]
    public void OrdinaryMigrationAssemblyDoesNotReferenceSafeMigrations()
    {
        // Arrange
        var assemblies = new[] { typeof(MigrationTests).Assembly, typeof(NestedSet<>).Assembly };

        // Act
        var references = assemblies
            .SelectMany(x => x.GetReferencedAssemblies())
            .Select(x => x.Name)
            .ToArray();

        // Assert
        Assert.DoesNotContain(references, x => x?.Contains("SafeMigrations", StringComparison.Ordinal) == true);
    }

    /// <summary>Checks application and structural indexes against their complete expected physical key order.</summary>
    private static void AssertCurrentIndexes(
        MigrationIndex[] indexes
    )
    {
        Assert.Equal(7, indexes.Length);
        Assert.Single(indexes, x => x.IsUnique && x.Columns.SequenceEqual(["tree_scope", "entry_id"]));
        Assert.All(indexes, x => Assert.True(x.IsOrdinary));
        var columns = indexes
            .Select(x => string.Join(",", x.Columns))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "entry_payload",
                "tree_scope,entry_id",
                "tree_scope,parent_entry",
                "tree_scope,tree_id,left_bound",
                "tree_scope,tree_id,parent_entry,entry_payload,entry_category,entry_id",
                "tree_scope,tree_id,parent_entry,sibling_position",
                "tree_scope,tree_id,right_bound",
            ],
            columns);
    }

    /// <summary>Checks the real provider catalog against the complete physical hierarchy contract.</summary>
    private static void AssertPhysicalContract(
        string engine,
        MigrationIndex[] indexes,
        MigrationCheck[] checks,
        MigrationForeignKey[] foreignKeys,
        IReadOnlyDictionary<string, string> columnTypes
    )
    {
        AssertCurrentIndexes(indexes);
        var order = Assert.Single(
            indexes,
            index => index.Columns.SequenceEqual(
            [
                "tree_scope",
                "tree_id",
                "parent_entry",
                "entry_payload",
                "entry_category",
                "entry_id",
            ]));

        Assert.Equal<bool>(
            [
                false,
                false,
                false,
                false,
                true,
                false,
            ],
            order.Descending);
        Assert.Equal(4, checks.Length);
        Assert.Contains(
            checks,
            check => check.Name.Contains("LeftMin", StringComparison.Ordinal)
                && NormalizeSql(check.Expression)
                    .Contains("left_bound>=1", StringComparison.Ordinal));
        Assert.Contains(
            checks,
            check => check.Name.Contains("RightAfterLeft", StringComparison.Ordinal)
                && NormalizeSql(check.Expression)
                    .Contains("right_bound>left_bound", StringComparison.Ordinal));
        Assert.Contains(
            checks,
            check => check.Name.Contains("DepthMin", StringComparison.Ordinal)
                && NormalizeSql(check.Expression)
                    .Contains("entry_depth>=0", StringComparison.Ordinal));
        Assert.Contains(
            checks,
            check => check.Name.Contains("PositionMin", StringComparison.Ordinal)
                && NormalizeSql(check.Expression)
                    .Contains("sibling_position>=0", StringComparison.Ordinal));

        var parent = Assert.Single(foreignKeys);
        Assert.Equal(["tree_scope", "parent_entry"], parent.DependentColumns);
        Assert.Equal(["tree_scope", "entry_id"], parent.PrincipalColumns);
        Assert.True(
            parent
                .DeleteAction
                .Replace('_', ' ')
                .ToUpperInvariant() is "NO ACTION" or "RESTRICT");

        var expectedInteger = engine == "Sqlite" ? "integer" : "bigint";
        Assert.Equal(expectedInteger, columnTypes["left_bound"], ignoreCase: true);
        Assert.Equal(expectedInteger, columnTypes["right_bound"], ignoreCase: true);
        Assert.Equal(expectedInteger, columnTypes["sibling_position"], ignoreCase: true);
    }

    /// <summary>Normalizes provider quoting and whitespace for semantic check-expression assertions.</summary>
    private static string NormalizeSql(
        string expression
    ) => Regex.Replace(expression.ToLowerInvariant(), "[\\s\\\"`\\[\\]()]", "", RegexOptions.CultureInvariant);

    /// <summary>Compares catalog definitions by value, including physical names and provider index facets.</summary>
    private static string[] IndexSignatures(
        IEnumerable<MigrationIndex> indexes
    ) => indexes
        .Select(x => $"{x.Name}:{string.Join(",", x.Columns)}:{string.Join(",", x.Descending)}:"
            + $"{x.IsUnique}:{x.IsOrdinary}")
        .ToArray();
}

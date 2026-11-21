namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>Verifies generated migrations independently of the optional SafeMigrations integration.</summary>
[DatabasePlatform]
public sealed partial class MigrationTests : IClassFixture<MigrationFixture>
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
    public async Task MigrateAsync_FreshMigration_CreatesOrderedIndexesWithCustomRelationalNames(
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

        var chain = database.Scaffold(MigrationStage.Current, "Initial");

        // Act
        await database.MigrateAsync(chain, cancellationToken: TestContext.Current.CancellationToken, target: null);
        var indexes = await database.ReadIndexesAsync(cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext(MigrationStage.Current, chain.Assembly);
        var migration = chain.GetMigration(context, 0);

        // Assert
        AssertCurrentIndexes(engine, indexes);
        Assert.Equal(
            engine == "PostgreSql"
                ? 7
                : 6,
            migration
                .UpOperations
                .OfType<CreateIndexOperation>()
                .Count());

        Assert.DoesNotContain("SafeMigrations", chain.LatestMigrationCode, StringComparison.Ordinal);
        Assert.All(indexes, x => Assert.True(x.Name.Length <= context.Model.GetMaxIdentifierLength()));
        Assert.Contains(indexes, x => x.Name == "application_payload_lookup");
    }

    /// <summary>Round-trips the generated snapshot without producing a phantom index migration.</summary>
    /// <param name="engine">The provider and real database engine to exercise.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task Scaffold_SubsequentScaffold_HasNoModelChanges(
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

        var initial = database.Scaffold(MigrationStage.Current, "Initial");
        await database.MigrateAsync(initial, cancellationToken: TestContext.Current.CancellationToken, target: null);

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
    public async Task Scaffold_SpecializedIndexAndFallback_SurviveSnapshotRoundTrip(
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

        var initial = database.Scaffold(MigrationStage.Specialized, "Specialized");
        await database.MigrateAsync(initial, cancellationToken: TestContext.Current.CancellationToken, target: null);

        // Act
        var next = database.Scaffold(MigrationStage.Specialized, "Unchanged", initial);
        await using var context = database.CreateContext(MigrationStage.Specialized, next.Assembly);
        var migration = next.GetMigration(context, 1);
        var indexes = await database.ReadIndexesAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var specialized = Assert.Single(indexes, x => x.Name == "application_filtered_left_lookup");
        Assert.Equal(["tree_scope", "tree_id", "left_bound"], specialized.Columns);
        Assert.False(specialized.IsOrdinary);

        var fallback = Assert.Single(
            indexes,
            index => index.Name != specialized.Name && index.Columns.SequenceEqual(specialized.Columns));

        Assert.Equal(engine != "PostgreSql", fallback.IsOrdinary);
        Assert.Equal(
            engine == "PostgreSql"
                ? 9
                : 8,
            indexes.Length);

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
    public async Task InsertRootAsync_MigratedSchema_SupportsNestedSetMutations(
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

        var chain = database.Scaffold(MigrationStage.Current, "Initial");
        await database.MigrateAsync(chain, cancellationToken: TestContext.Current.CancellationToken, target: null);
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

        var nodes = await database.ReadNodesAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Current);

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

    /// <summary>Qualifies Int64 storage, checks, self-FK metadata, and mixed ordering on a migrated database.</summary>
    /// <param name="engine">The provider and real database engine to exercise.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task InsertNodeDirectlyAsync_FreshMigration_CreatesTheCompletePhysicalTreeContract(
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

        var chain = database.Scaffold(MigrationStage.Current, "PhysicalContract");
        await database.MigrateAsync(chain, cancellationToken: TestContext.Current.CancellationToken, target: null);
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
        await database.InsertNodeDirectlyAsync(node, cancellationToken: TestContext.Current.CancellationToken);
        var nodes = await database.ReadNodesAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Current);
        var indexes = await database.ReadIndexesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var checks = await database.ReadChecksAsync(cancellationToken: TestContext.Current.CancellationToken);
        var foreignKeys = await database.ReadForeignKeysAsync(cancellationToken: TestContext.Current.CancellationToken);
        var columnTypes = await database.ReadColumnTypesAsync(cancellationToken: TestContext.Current.CancellationToken);

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
    public async Task InsertNodeDirectlyAsync_GeneratedChecks_RejectInvalidLeftBoundary(
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

        var chain = database.Scaffold(MigrationStage.Current, "CheckEnforcement");
        await database.MigrateAsync(chain, cancellationToken: TestContext.Current.CancellationToken, target: null);
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
        var exception = await Record.ExceptionAsync(() => database.InsertNodeDirectlyAsync(
            invalid,
            cancellationToken: TestContext.Current.CancellationToken));

        var nodes = await database.ReadNodesAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Current);

        // Assert
        Assert.IsType<DbException>(exception, exactMatch: false);
        Assert.Empty(nodes);
    }

    /// <summary>Checks that ordinary migration validation does not acquire optional integration dependencies.</summary>
    [Fact]
    public void GetReferencedAssemblies_OrdinaryMigrationAssembly_DoesNotReferenceSafeMigrations()
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
}

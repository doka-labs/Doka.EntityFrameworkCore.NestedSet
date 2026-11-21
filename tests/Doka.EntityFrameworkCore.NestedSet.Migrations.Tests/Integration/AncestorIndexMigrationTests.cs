namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>Qualifies the optional generated ancestor index through ordinary EF migrations.</summary>
[DatabasePlatform]
[Collection(AncestorIndexFixtureRegistration.Name)]
public sealed partial class AncestorIndexMigrationTests
{
    private readonly MigrationFixture _fixture;

    /// <summary>Creates isolated migration scenarios using lazily shared real engines.</summary>
    /// <param name="fixture">The engine owner supplied by xUnit.</param>
    public AncestorIndexMigrationTests(
        MigrationFixture fixture
    )
    {
        _fixture = fixture;
    }

    /// <summary>Creates the shadow flag and ordered additive access path without an optional adapter.</summary>
    /// <param name="engine">The real relational engine.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task MigrateAsync_FreshMigration_CreatesComputedFlagAndOrderedIndex(
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

        var chain = database.Scaffold(true, "AncestorInitial");

        // Act
        await database.MigrateAsync(
            chain,
            cancellationToken: TestContext.Current.CancellationToken,
            enabled: true,
            target: null,
            adoptIndex: false,
            enableIntegration: true,
            splitStructure: false);
        await database.PhysicalDatabase.SeedAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Current);
        var indexes = await database.PhysicalDatabase.ReadIndexesAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await using var context = database.CreateContext(assembly: chain.Assembly);
        var flags = await ReadFlagsAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        var migration = chain.GetMigration(context, 0);
        var column = migration
            .UpOperations
            .OfType<CreateTableOperation>()
            .Single(x => x.Name == MigrationContext.TableName)
            .Columns
            .Single(x => x.Name == AncestorIndexContext.FlagColumn);

        // Assert
        Assert.NotNull(column.ComputedColumnSql);
        Assert.False(column.IsNullable);
        Assert.Equal([true, false], flags);
        AssertAncestorIndex(indexes, engine != "PostgreSql");
        Assert.DoesNotContain("SafeMigrations", chain.LatestMigrationCode, StringComparison.Ordinal);
    }

    /// <summary>Adds the generated flag to populated tables without changing existing values or indexes.</summary>
    /// <param name="engine">The real relational engine.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task MigrateAsync_PopulatedUpgrade_PreservesDataAndExistingAccessPaths(
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

        var indexesBefore = await database.PhysicalDatabase.ReadIndexesAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var upgrade = database.Scaffold(true, "BAddAncestorIndex", initial);

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

        var indexesAfter = await database.PhysicalDatabase.ReadIndexesAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await using var context = database.CreateContext(assembly: upgrade.Assembly);
        var flags = await ReadFlagsAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        var migration = upgrade.GetMigration(context, 1);

        // Assert
        Assert.Equal(before, after);
        Assert.Equal([true, false], flags);
        AssertAncestorIndex(indexesAfter, engine != "PostgreSql");
        Assert.All(
            indexesBefore,
            old => Assert.Contains(
                indexesAfter,
                current =>
                    current.Name == old.Name && current.Columns.SequenceEqual(old.Columns)));

        Assert.DoesNotContain(migration.UpOperations, operation => operation is DropTableOperation);
        Assert.Single(
            migration.UpOperations.OfType<AddColumnOperation>(),
            column => column is { Name: AncestorIndexContext.FlagColumn, ComputedColumnSql: not null });
    }

    /// <summary>Retains generated SQL and application-owned indexes across real snapshot compilation.</summary>
    /// <param name="engine">The real relational engine.</param>
    /// <param name="adoptIndex">Whether an equivalent application index is adopted.</param>
    [Theory]
    [InlineData("Sqlite", false)]
    [InlineData("Sqlite", true)]
    [InlineData("MySql", false)]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", false)]
    [InlineData("MariaDb", true)]
    [InlineData("PostgreSql", false)]
    [InlineData("PostgreSql", true)]
    [InlineData("SqlServer", false)]
    [InlineData("SqlServer", true)]
    public async Task Scaffold_Snapshot_RetainsComputedFlagWithoutPhantomMigration(
        string engine,
        bool adoptIndex
    )
    {
        // Arrange
        await using var database = await AncestorIndexDatabase.CreateAsync(
            _fixture,
            engine,
            cancellationToken: TestContext.Current.CancellationToken,
            configureOptions: null,
            configureDesignServices: null);

        var initial = database.Scaffold(true, "AncestorInitial", adoptIndex: adoptIndex);
        await database.MigrateAsync(
            initial,
            adoptIndex: adoptIndex,
            cancellationToken: TestContext.Current.CancellationToken,
            enabled: true,
            target: null,
            enableIntegration: true,
            splitStructure: false);

        // Act
        var next = database.Scaffold(true, "Unchanged", initial, adoptIndex);
        await using var context = database.CreateContext(true, next.Assembly, adoptIndex);
        var migration = next.GetMigration(context, 1);
        var indexes = await database.PhysicalDatabase.ReadIndexesAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var pending = context
            .GetService<IMigrator>()
            .HasPendingModelChanges();

        // Assert
        Assert.Empty(migration.UpOperations);
        Assert.Empty(migration.DownOperations);
        Assert.False(pending);
        var ancestor = AssertAncestorIndex(indexes, engine != "PostgreSql" || adoptIndex);

        if (adoptIndex)
        {
            Assert.Equal("application_ancestor_lookup", ancestor.Name);
        }
    }

    /// <summary>Rejects a same-name conflicting index without modifying persisted hierarchy data.</summary>
    /// <param name="engine">The real relational engine.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task MigrateAsync_ConflictingIndex_RejectsUpgradeAndPreservesData(
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
        await using var context = database.CreateContext(assembly: upgrade.Assembly);
        var index = Assert.Single(
            upgrade
                .GetMigration(context, 1)
                .UpOperations
                .OfType<CreateIndexOperation>());

        var sql = context.GetService<ISqlGenerationHelper>();

        // WHY: The adversarial index deliberately bypasses migrations; fixed metadata identifiers are
        // provider-delimited because identifiers cannot be supplied as value parameters.
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
            enabled: true,
            target: null,
            adoptIndex: false,
            enableIntegration: true,
            splitStructure: false));

        var after = await database.PhysicalDatabase.ReadNodesAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Current);

        var applied = await context.Database.GetAppliedMigrationsAsync(CancellationToken.None);

        // Assert
        Assert.IsType<DbException>(exception, exactMatch: false);
        Assert.Equal(before, after);
        Assert.Equal(initial.MigrationIds, applied);
    }

    /// <summary>Reads generated values independently of tracked entity state.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    private static Task<bool[]> ReadFlagsAsync(
        AncestorIndexContext context,
        CancellationToken cancellationToken
    ) => context
        .Set<MigrationNode>()
        .OrderBy(x => x.NodeId)
        .Select(x => EF.Property<bool>(x, AncestorIndexContext.FlagProperty))
        .ToArrayAsync(cancellationToken);

    /// <summary>Checks the complete ordered native index rather than its name alone.</summary>
    private static MigrationIndex AssertAncestorIndex(
        MigrationIndex[] indexes,
        bool ordinary
    )
    {
        var index = Assert.Single(
            indexes,
            x => x.Columns.SequenceEqual(
            [
                "tree_scope",
                AncestorIndexContext.FlagColumn,
                "tree_id",
                "right_bound",
                "left_bound",
                "entry_id",
            ]));

        Assert.False(index.IsUnique);
        Assert.Equal(ordinary, index.IsOrdinary);

        return index;
    }

    /// <summary>Captures stable physical definitions for before/after comparisons.</summary>
    private static string[] Signatures(
        MigrationIndex[] indexes
    ) => indexes
        .Select(x => x.Name + ":" + string.Join(",", x.Columns) + ":" + x.IsUnique)
        .Order(StringComparer.Ordinal)
        .ToArray();
}

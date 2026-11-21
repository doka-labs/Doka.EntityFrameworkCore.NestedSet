namespace Doka.EntityFrameworkCore.NestedSet.SafeMigrations.Tests;

/// <summary>Qualifies the optional ancestor index when applications also enable SafeMigrations.</summary>
[DatabasePlatform]
public sealed partial class AncestorIndexSafeMigrationTests : IClassFixture<MigrationFixture>
{
    private readonly MigrationFixture _fixture;
    private readonly ITestOutputHelper _output;

    /// <summary>Creates independent optional-adapter scenarios on reusable engines.</summary>
    /// <param name="fixture">The ordinary migration fixture.</param>
    /// <param name="output">The retained diagnostic output for known adapter boundaries.</param>
    public AncestorIndexSafeMigrationTests(
        MigrationFixture fixture,
        ITestOutputHelper output
    )
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>Accepts and safely replays the exact generated ancestor-index definition.</summary>
    /// <param name="engine">The real relational engine and its optional adapter.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task ExecuteOperationsAsync_GeneratedAncestorIndex_ReplaysWithoutChangingData(
        string engine
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync(
            engine,
            cancellationToken: TestContext.Current.CancellationToken);

        var chain = database.Scaffold(true, "AncestorSafeInitial");

        // WHY: Index replay also has to work on an ordinary EF-created schema; it must not depend on
        // the optional adapter having created the table or adjusted SQLite's generated-column facets.
        var ordinary = database.Scaffold(true, "AncestorOrdinaryInitial", enableIntegration: false);
        await database.MigrateAsync(
            ordinary,
            enableIntegration: false,
            cancellationToken: TestContext.Current.CancellationToken,
            enabled: true,
            target: null,
            adoptIndex: false,
            splitStructure: false);

        await database.PhysicalDatabase.SeedAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Current);
        await using var context = database.CreateContext(assembly: chain.Assembly);
        var operation = AncestorOperation(chain, context);
        var before = await database.PhysicalDatabase.ReadNodesAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Current);

        var indexesBefore = IndexSignatures(
            await database.PhysicalDatabase.ReadIndexesAsync(
                cancellationToken: TestContext.Current.CancellationToken));

        var historyBefore = (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();

        // Act
        await SafeMigrationTestServices.ExecuteOperationsAsync(
            context,
            [operation],
            cancellationToken: TestContext.Current.CancellationToken);

        var report = await context
            .GetService<ISafeMigrationRunner>()
            .AnalyzeAsync(
                context,
                [operation],
                new SafeMigrationRunOptions("nested-set-ancestor-index-replay"),
                CancellationToken.None);

        var after = await database.PhysicalDatabase.ReadNodesAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Current);

        var indexesAfter = IndexSignatures(
            await database.PhysicalDatabase.ReadIndexesAsync(
                cancellationToken: TestContext.Current.CancellationToken));

        var historyAfter = (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();

        // Assert
        Assert.Equal(before, after);
        Assert.Equal(indexesBefore, indexesAfter);
        Assert.Equal(ordinary.MigrationIds, historyBefore);
        Assert.Equal(historyBefore, historyAfter);
        Assert.Equal(SafeMigrationReportStatus.Ready, report.Status);
        var assessment = Assert.Single(report.Assessments);
        Assert.Equal(SafeMigrationObservedState.Matching, assessment.ObservedState);
        Assert.Equal(SafeMigrationAction.NoOp, assessment.Action);
        Assert.Equal(
            engine == "PostgreSql"
                ? "tree_id IS NOT NULL"
                : null,
            ((EnsureIndexIntent)operation.Intent).Definition.Filter);
    }

    /// <summary>Rejects an externally replaced ancestor index while retaining all application data.</summary>
    /// <param name="engine">The real relational engine and its optional adapter.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task AnalyzeAsync_AncestorIndexDrift_FailsClosedAndPreservesData(
        string engine
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync(
            engine,
            cancellationToken: TestContext.Current.CancellationToken);

        var chain = database.Scaffold(true, "AncestorSafeInitial");
        var ordinary = database.Scaffold(true, "AncestorOrdinaryInitial", enableIntegration: false);
        await database.MigrateAsync(
            ordinary,
            enableIntegration: false,
            cancellationToken: TestContext.Current.CancellationToken,
            enabled: true,
            target: null,
            adoptIndex: false,
            splitStructure: false);

        await database.PhysicalDatabase.SeedAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Current);
        await using var context = database.CreateContext(assembly: chain.Assembly);
        var operation = AncestorOperation(chain, context);
        var definition = ((EnsureIndexIntent)operation.Intent).Definition;
        await SafeMigrationTestServices.IntroduceIndexDriftAsync(
            context,
            definition,
            "entry_depth",
            cancellationToken: TestContext.Current.CancellationToken);

        var before = await database.PhysicalDatabase.ReadNodesAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Current);

        var indexesBefore = IndexSignatures(
            await database.PhysicalDatabase.ReadIndexesAsync(
                cancellationToken: TestContext.Current.CancellationToken));

        var historyBefore = (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();

        // Act
        var report = await context
            .GetService<ISafeMigrationRunner>()
            .AnalyzeAsync(
                context,
                [operation],
                new SafeMigrationRunOptions("nested-set-ancestor-index-drift"),
                CancellationToken.None);

        var exception = await Record.ExceptionAsync(() =>
            SafeMigrationTestServices.ExecuteOperationsAsync(
                context,
                [operation],
                cancellationToken: TestContext.Current.CancellationToken));

        var after = await database.PhysicalDatabase.ReadNodesAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            stage: MigrationStage.Current);

        var indexesAfter = IndexSignatures(
            await database.PhysicalDatabase.ReadIndexesAsync(
                cancellationToken: TestContext.Current.CancellationToken));

        var historyAfter = (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();

        // Assert
        Assert.NotNull(exception);
        Assert.Equal(SafeMigrationReportStatus.Blocked, report.Status);
        var assessment = Assert.Single(report.Assessments);
        Assert.Equal(SafeMigrationObservedState.Different, assessment.ObservedState);
        Assert.Equal(SafeMigrationAction.RejectDifferent, assessment.Action);
        Assert.Equal(before, after);
        Assert.Equal(indexesBefore, indexesAfter);
        Assert.Equal(ordinary.MigrationIds, historyBefore);
        Assert.Equal(historyBefore, historyAfter);
    }

    /// <summary>Configures both provider-specific adapter registration paths on an isolated database.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="engine">The selected database engine.</param>
    private Task<AncestorIndexDatabase> CreateDatabaseAsync(
        string engine,
        CancellationToken cancellationToken
    ) => AncestorIndexDatabase.CreateAsync(
        _fixture,
        engine,
        cancellationToken: cancellationToken,
        configureOptions: options => SafeMigrationTestServices.ConfigureOptions(options, engine),
        configureDesignServices: SafeMigrationTestServices.ConfigureDesignServices);

    /// <summary>Finds the actual scaffolded safe intent by the complete ordered key definition.</summary>
    private static SafeMigrationOperation AncestorOperation(
        MigrationChain chain,
        DbContext context,
        int migrationIndex = 0
    ) => Assert.Single(
        chain
            .GetMigration(context, migrationIndex)
            .UpOperations
            .OfType<SafeMigrationOperation>(),
        operation => operation.Intent is EnsureIndexIntent intent
            && intent
                .Definition
                .Keys
                .Select(key => key.Column)
                .SequenceEqual(
                [
                    "tree_scope",
                    AncestorIndexContext.FlagColumn,
                    "tree_id",
                    "right_bound",
                    "left_bound",
                    "entry_id",
                ]));

    /// <summary>Captures complete physical index keys for adversarial before/after comparisons.</summary>
    private static string[] IndexSignatures(
        MigrationIndex[] indexes
    ) => indexes
        .Select(x =>
            x.Name
            + ":"
            + string.Join(",", x.Columns)
            + ":"
            + string.Join(",", x.Descending)
            + ":"
            + x.IsUnique
            + ":"
            + x.IsOrdinary)
        .Order(StringComparer.Ordinal)
        .ToArray();
}

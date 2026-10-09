namespace Doka.EntityFrameworkCore.NestedSet.SafeMigrations.Tests;

public sealed partial class SafeMigrationTests
{
    /// <summary>Verifies the safe scaffolder preserves a stable snapshot without repeated index changes.</summary>
    /// <param name="engine">The relational database engine.</param>
    /// <param name="qualifySchema">Whether the model preserves an explicit schema or database qualifier.</param>
    /// <returns>A task that completes after scaffolding against the unchanged generated snapshot.</returns>
    [Theory]
    [InlineData("Sqlite", false)]
    [InlineData("PostgreSql", true)]
    [InlineData("SqlServer", false)]
    [InlineData("SqlServer", true)]
    [InlineData("MySql", false)]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", false)]
    [InlineData("MariaDb", true)]
    public async Task UnchangedSnapshotProducesNoMigrationOperations(
        string engine,
        bool qualifySchema
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync(engine, qualifySchema);
        var initial = database.Scaffold(MigrationStage.Current, "SafeInitial");

        // Act
        var unchanged = database.Scaffold(MigrationStage.Current, "SafeUnchanged", initial);
        await using var context = database.CreateContext(MigrationStage.Current, unchanged.Assembly);
        var migration = unchanged.GetMigration(context, 1);
        var hasPendingModelChanges = context.Database.HasPendingModelChanges();

        // Assert
        Assert.Empty(migration.UpOperations);
        Assert.Empty(migration.DownOperations);
        Assert.False(hasPendingModelChanges);
    }

    /// <summary>Verifies each adapter contributes exactly one package-generated tooling registration.</summary>
    /// <param name="provider">The exact provider name used by EF's design-time discovery.</param>
    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite")]
    [InlineData("Npgsql.EntityFrameworkCore.PostgreSQL")]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer")]
    [InlineData("Doka.EntityFrameworkCore.MySql")]
    public void PackageBuildAssetsRegisterProviderDesignServices(
        string provider
    )
    {
        // Arrange
        var assembly = typeof(SafeMigrationTests).Assembly;

        // Act
        var registrations = assembly
            .GetCustomAttributes<DesignTimeServicesReferenceAttribute>()
            .Where(attribute => attribute.ForProvider == provider)
            .ToArray();

        // Assert
        var registration = Assert.Single(registrations);
        var serviceType = Type.GetType(registration.TypeName, throwOnError: true)!;
        Assert.Contains(
            "SafeMigrations",
            serviceType.Assembly.GetName()
                .Name!,
            StringComparison.Ordinal);
        Assert.IsAssignableFrom<IDesignTimeServices>(Activator.CreateInstance(serviceType));
    }

    /// <summary>Verifies SQLite safe script generation rejects an upgrade without changing stored state.</summary>
    /// <param name="options">The ordinary or idempotent script-generation mode.</param>
    /// <param name="expectedMessage">The rejection emitted by the first applicable SQLite script guard.</param>
    /// <returns>A task that completes after inspecting the unmodified catalog, rows, and migration history.</returns>
    [Theory]
    [InlineData(MigrationsSqlGenerationOptions.Script, "cannot express")]
    [InlineData(
        MigrationsSqlGenerationOptions.Script | MigrationsSqlGenerationOptions.Idempotent,
        "Generating idempotent scripts for migrations is not")]
    public async Task SqliteSafeScriptGenerationIsRejectedBeforeMutation(
        MigrationsSqlGenerationOptions options,
        string expectedMessage
    )
    {
        // Arrange
        await using var database = await CreateDatabaseAsync("Sqlite", false);
        var baseline = database.Scaffold(MigrationStage.Baseline, "SafeBaseline");
        await database.MigrateAsync(baseline);
        await database.SeedAsync();
        var upgrade = database.Scaffold(MigrationStage.Current, "SafeScopeKey", baseline);
        await using var context = database.CreateContext(MigrationStage.Current, upgrade.Assembly);
        var migrator = context.GetService<IMigrator>();
        var indexesBefore = IndexSignatures(await database.ReadIndexesAsync());
        var nodesBefore = await database.ReadNodesAsync();
        var historyBefore = (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();

        // WHY: SafeMigrations rejects ordinary scripts; EF's SQLite history guard rejects idempotent scripts first.
        // Act
        var exception = Record.Exception(() => migrator.GenerateScript(baseline.MigrationIds[0], options: options));

        // Assert
        var scriptException = Assert.IsType<NotSupportedException>(exception);
        Assert.Contains(expectedMessage, scriptException.Message, StringComparison.Ordinal);
        Assert.Equal(indexesBefore, IndexSignatures(await database.ReadIndexesAsync()));
        Assert.Equal(nodesBefore, await database.ReadNodesAsync());
        Assert.Equal([baseline.MigrationIds[0]], historyBefore);
        Assert.Equal(historyBefore, await context.Database.GetAppliedMigrationsAsync(CancellationToken.None));
    }
}

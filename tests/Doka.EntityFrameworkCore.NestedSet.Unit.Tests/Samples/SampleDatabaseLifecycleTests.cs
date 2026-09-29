namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

/// <summary>Exercises migration, reset and inspection boundaries against isolated persistent SQLite files.</summary>
public sealed class SampleDatabaseLifecycleTests
{
    /// <summary>The first execution applies migrations and creates the fixed sample file.</summary>
    [Fact]
    public async Task FreshRunAppliesTheOwnedMigrationChain()
    {
        // Arrange
        await using var directory = new SampleLifecycleDirectory();
        var configuration = directory.Configuration();
        await using var context = SampleLifecycleDirectory.CreateContext(configuration);

        // Act
        await SampleDatabase.PrepareAsync(context, configuration, reset: false, CancellationToken.None);

        // Assert
        var migrations = await context.Database.GetAppliedMigrationsAsync(CancellationToken.None);

        Assert.True(File.Exists(configuration.SqlitePath));
        Assert.Equal(["20260929000000_SampleLifecycleInitial"], migrations);
        Assert.Empty(
            await context
                .Sentinels
                .AsNoTracking()
                .ToListAsync(CancellationToken.None));
    }

    /// <summary>An explicit reset removes prior application rows while applying the complete schema again.</summary>
    [Fact]
    public async Task ExplicitResetRecreatesTheSchemaWithoutEarlierRows()
    {
        // Arrange
        await using var directory = new SampleLifecycleDirectory();
        var configuration = directory.Configuration();
        await using var context = SampleLifecycleDirectory.CreateContext(configuration);
        await SampleLifecycleDirectory.SeedAsync(context, "earlier-run", CancellationToken.None);

        // Act
        await SampleDatabase.PrepareAsync(context, configuration, reset: true, CancellationToken.None);

        // Assert
        var migrations = await context.Database.GetAppliedMigrationsAsync(CancellationToken.None);

        Assert.Equal(["20260929000000_SampleLifecycleInitial"], migrations);
        Assert.Empty(
            await context
                .Sentinels
                .AsNoTracking()
                .ToListAsync(CancellationToken.None));
    }

    /// <summary>Resetting one sample does not delete a different sample's persistent results.</summary>
    [Fact]
    public async Task ExplicitResetPreservesAnotherSamplesResults()
    {
        // Arrange
        await using var directory = new SampleLifecycleDirectory();
        var configuration = directory.Configuration();
        var otherConfiguration = directory.Configuration("kpis");
        await using var context = SampleLifecycleDirectory.CreateContext(configuration);
        await using var otherContext = SampleLifecycleDirectory.CreateContext(otherConfiguration);
        await SampleLifecycleDirectory.SeedAsync(context, "filesystem-data", CancellationToken.None);
        await SampleLifecycleDirectory.SeedAsync(otherContext, "kpi-data", CancellationToken.None);

        // Act
        await SampleDatabase.PrepareAsync(context, configuration, reset: true, CancellationToken.None);

        // Assert
        var preserved = await otherContext
            .Sentinels
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        Assert.Equal("kpi-data", preserved.Value);
        Assert.Empty(
            await context
                .Sentinels
                .AsNoTracking()
                .ToListAsync(CancellationToken.None));
    }

    /// <summary>A second run explains reset or inspection instead of appending data or mutating the schema.</summary>
    [Fact]
    public async Task ExistingResultsRequireAnExplicitReset()
    {
        // Arrange
        await using var directory = new SampleLifecycleDirectory();
        var configuration = directory.Configuration();
        await using var context = SampleLifecycleDirectory.CreateContext(configuration);
        await SampleLifecycleDirectory.SeedAsync(context, "preserved", CancellationToken.None);

        // Act
        var error = await Record.ExceptionAsync(() => SampleDatabase.PrepareAsync(
            context,
            configuration,
            reset: false,
            CancellationToken.None));

        // Assert
        var invalid = Assert.IsType<InvalidOperationException>(error);
        var preserved = await context
            .Sentinels
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        Assert.Contains("--inspect", invalid.Message, StringComparison.Ordinal);
        Assert.Contains("--reset", invalid.Message, StringComparison.Ordinal);
        Assert.Equal("preserved", preserved.Value);
        Assert.Single(await context.Database.GetAppliedMigrationsAsync(CancellationToken.None));
    }

    /// <summary>Migration history identifies an earlier run even when every application row was deleted.</summary>
    [Fact]
    public async Task EmptyApplicationTablesStillRequireAnExplicitReset()
    {
        // Arrange
        await using var directory = new SampleLifecycleDirectory();
        var configuration = directory.Configuration();
        await using var context = SampleLifecycleDirectory.CreateContext(configuration);
        await SampleLifecycleDirectory.SeedAsync(context, "removed", CancellationToken.None);
        context.Sentinels.Remove(await context.Sentinels.SingleAsync(CancellationToken.None));
        await context.SaveChangesAsync(CancellationToken.None);

        // Act
        var error = await Record.ExceptionAsync(() => SampleDatabase.PrepareAsync(
            context,
            configuration,
            reset: false,
            CancellationToken.None));

        // Assert
        var invalid = Assert.IsType<InvalidOperationException>(error);

        Assert.Contains("--reset", invalid.Message, StringComparison.Ordinal);
        Assert.Empty(
            await context
                .Sentinels
                .AsNoTracking()
                .ToListAsync(CancellationToken.None));

        Assert.Single(await context.Database.GetAppliedMigrationsAsync(CancellationToken.None));
    }

    /// <summary>A context bound to a different file cannot reset it under another sample's ownership.</summary>
    [Fact]
    public async Task ForeignSqliteContextIsRejectedAndItsDataSurvives()
    {
        // Arrange
        await using var directory = new SampleLifecycleDirectory();
        var configuration = directory.Configuration();
        var foreignConfiguration = directory.Configuration("usergroups");
        await using var foreignContext = SampleLifecycleDirectory.CreateContext(foreignConfiguration);
        await SampleLifecycleDirectory.SeedAsync(foreignContext, "foreign-data", CancellationToken.None);

        // Act
        var error = await Record.ExceptionAsync(() => SampleDatabase.PrepareAsync(
            foreignContext,
            configuration,
            reset: true,
            CancellationToken.None));

        // Assert
        var invalid = Assert.IsType<InvalidOperationException>(error);
        var preserved = await foreignContext
            .Sentinels
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        Assert.Contains("owned SQLite file", invalid.Message, StringComparison.Ordinal);
        Assert.Equal("foreign-data", preserved.Value);
        Assert.False(File.Exists(configuration.SqlitePath));
    }

    /// <summary>Server ownership is checked before a destructive command or connection attempt can occur.</summary>
    [Fact]
    public async Task ForeignServerDatabaseIsRejectedBeforeConnecting()
    {
        // Arrange
        const string endpoint = "Server=127.0.0.1;Port=1;User ID=sample;Password=test-secret;";
        var configuration = SampleDatabaseConfiguration.Create("filesystem", SampleProvider.MariaDb, endpoint);
        var options = new DbContextOptionsBuilder<SampleLifecycleContext>().UseMySql(
            endpoint + "Database=application;",
            MySqlServerVersion.MariaDb(new Version(11, 8, 0))).Options;

        await using var context = new SampleLifecycleContext(options);

        // Act
        var error = await Record.ExceptionAsync(() => SampleDatabase.PrepareAsync(
            context,
            configuration,
            reset: true,
            CancellationToken.None));

        // Assert
        var invalid = Assert.IsType<InvalidOperationException>(error);

        Assert.Contains("owned server database", invalid.Message, StringComparison.Ordinal);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>An already canceled reset leaves the owned data and schema intact.</summary>
    [Fact]
    public async Task CanceledResetPreservesEarlierData()
    {
        // Arrange
        await using var directory = new SampleLifecycleDirectory();
        var configuration = directory.Configuration();
        await using var context = SampleLifecycleDirectory.CreateContext(configuration);
        await SampleLifecycleDirectory.SeedAsync(context, "preserved", CancellationToken.None);
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();

        // Act
        var error = await Record.ExceptionAsync(() => SampleDatabase.PrepareAsync(
            context,
            configuration,
            reset: true,
            stop.Token));

        // Assert
        Assert.IsType<OperationCanceledException>(error, exactMatch: false);
        Assert.Equal(
            "preserved",
            (await context
                .Sentinels
                .AsNoTracking()
                .SingleAsync(CancellationToken.None)).Value);

        Assert.Single(await context.Database.GetAppliedMigrationsAsync(CancellationToken.None));
    }

    /// <summary>An already canceled first run does not create its storage directory or file.</summary>
    [Fact]
    public async Task CanceledFirstRunDoesNotCreateStorage()
    {
        // Arrange
        await using var directory = new SampleLifecycleDirectory();
        var configuration = directory.Configuration();
        await using var context = SampleLifecycleDirectory.CreateContext(configuration);
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();

        // Act
        var error = await Record.ExceptionAsync(() => SampleDatabase.PrepareAsync(
            context,
            configuration,
            reset: false,
            stop.Token));

        // Assert
        Assert.IsType<OperationCanceledException>(error, exactMatch: false);
        Assert.False(Directory.Exists(directory.DirectoryPath));
        Assert.False(File.Exists(configuration.SqlitePath));
    }

    /// <summary>Inspection never creates a missing file, including with mistakenly writable options.</summary>
    /// <param name="readOnly">Whether the supplied context enforces a read-only connection.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingInspectionDoesNotCreateAFile(
        bool readOnly
    )
    {
        // Arrange
        await using var directory = new SampleLifecycleDirectory();
        var configuration = directory.Configuration();

        // WHY: An existing parent ensures a mistaken read/write connection could actually create the missing file.
        Directory.CreateDirectory(directory.DirectoryPath);
        await using var context = SampleLifecycleDirectory.CreateContext(configuration, readOnly);

        // Act
        var error = await Record.ExceptionAsync(() =>
            SampleDatabase.EnsureAvailableForInspectionAsync(context, CancellationToken.None));

        // Assert
        var invalid = Assert.IsType<InvalidOperationException>(error);

        Assert.Contains("Run a scenario first", invalid.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(configuration.SqlitePath));
        Assert.Empty(Directory.GetFiles(directory.DirectoryPath));
    }

    /// <summary>Inspection rejects an unmigrated database without changing its schema or rows.</summary>
    [Fact]
    public async Task InspectionRejectsUnmigratedDataWithoutChangingIt()
    {
        // Arrange
        await using var directory = new SampleLifecycleDirectory();
        var configuration = directory.Configuration();

        // WHY: The missing history is the defect under test; inspection must not adopt or migrate this existing schema.
        Directory.CreateDirectory(directory.DirectoryPath);
        await using (var setup = SampleLifecycleDirectory.CreateContext(configuration))
        {
            await setup.Database.EnsureCreatedAsync(CancellationToken.None);
            setup.Sentinels.Add(
                new SampleLifecycleSentinel
                {
                    Id = 1,
                    Value = "unmigrated",
                });

            await setup.SaveChangesAsync(CancellationToken.None);
        }

        var original = await File.ReadAllBytesAsync(configuration.SqlitePath!, CancellationToken.None);
        await using var context = SampleLifecycleDirectory.CreateContext(configuration, readOnly: true);

        // Act
        var error = await Record.ExceptionAsync(() =>
            SampleDatabase.EnsureAvailableForInspectionAsync(context, CancellationToken.None));

        // Assert
        var invalid = Assert.IsType<InvalidOperationException>(error);
        var preserved = await context
            .Sentinels
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        var actual = await File.ReadAllBytesAsync(configuration.SqlitePath!, CancellationToken.None);

        Assert.Contains("Run a scenario first", invalid.Message, StringComparison.Ordinal);
        Assert.Equal("unmigrated", preserved.Value);
        Assert.Equal(original, actual);
        Assert.Empty(await context.Database.GetAppliedMigrationsAsync(CancellationToken.None));
    }

    /// <summary>Read-only inspection preserves rows and the complete database file.</summary>
    [Fact]
    public async Task InspectionPreservesExistingDataAndSchema()
    {
        // Arrange
        await using var directory = new SampleLifecycleDirectory();
        var configuration = directory.Configuration();
        await using (var setup = SampleLifecycleDirectory.CreateContext(configuration))
        {
            await SampleLifecycleDirectory.SeedAsync(setup, "inspection-data", CancellationToken.None);
        }

        var original = await File.ReadAllBytesAsync(configuration.SqlitePath!, CancellationToken.None);
        await using var context = SampleLifecycleDirectory.CreateContext(configuration, readOnly: true);

        // Act
        await SampleDatabase.EnsureAvailableForInspectionAsync(context, CancellationToken.None);

        // Assert
        var preserved = await context
            .Sentinels
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        var actual = await File.ReadAllBytesAsync(configuration.SqlitePath!, CancellationToken.None);

        Assert.Equal("inspection-data", preserved.Value);
        Assert.Equal(original, actual);
        Assert.Single(await context.Database.GetAppliedMigrationsAsync(CancellationToken.None));
    }
}

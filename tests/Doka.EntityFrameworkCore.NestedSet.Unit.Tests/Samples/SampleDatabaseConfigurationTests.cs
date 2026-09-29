namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

/// <summary>Keeps sample database ownership independent of machine environment and endpoint overrides.</summary>
public sealed class SampleDatabaseConfigurationTests
{
    private const string ServerEndpoint = "Server=127.0.0.1;Port=33079;User ID=sample;Password=test-secret;";

    /// <summary>Every server route adds only the fixed database belonging to the chosen sample.</summary>
    /// <param name="sample">The known sample project.</param>
    /// <param name="provider">The server route.</param>
    /// <param name="database">The independently expected owned database.</param>
    [Theory]
    [InlineData("filesystem", "MariaDb", "nestedset_sample_filesystem")]
    [InlineData("filesystem", "MySql", "nestedset_sample_filesystem")]
    [InlineData("kpis", "MariaDb", "nestedset_sample_kpis")]
    [InlineData("kpis", "MySql", "nestedset_sample_kpis")]
    [InlineData("usergroups", "MariaDb", "nestedset_sample_usergroups")]
    [InlineData("usergroups", "MySql", "nestedset_sample_usergroups")]
    public async Task ServerEndpointReceivesOnlyTheOwnedDatabase(
        string sample,
        string provider,
        string database
    )
    {
        // Arrange
        var route = Enum.Parse<SampleProvider>(provider);

        // Act
        var configuration = SampleDatabaseConfiguration.Create(sample, route, ServerEndpoint);

        // Assert
        await using var context = new SampleLifecycleContext(configuration.CreateOptions<SampleLifecycleContext>());
        var connection = new MySqlConnectionStringBuilder(
            context.Database.GetDbConnection().ConnectionString);

        Assert.Equal(database, configuration.DatabaseName);
        Assert.Equal(database, connection.Database);
        Assert.Equal("127.0.0.1", connection.Server);
        Assert.Equal(33079U, connection.Port);
        Assert.Null(configuration.SqlitePath);
        Assert.DoesNotContain("test-secret", configuration.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", configuration.Description, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An endpoint can state the exact owned database without weakening reset ownership.</summary>
    /// <param name="provider">The documented server route.</param>
    [Theory]
    [InlineData("MariaDb")]
    [InlineData("MySql")]
    public void ExactDatabaseOverrideIsAccepted(
        string provider
    )
    {
        // Arrange
        var route = Enum.Parse<SampleProvider>(provider);
        var endpoint = ServerEndpoint + "Database=nestedset_sample_filesystem;";

        // Act
        var configuration = SampleDatabaseConfiguration.Create("filesystem", route, endpoint);

        // Assert
        Assert.Equal("nestedset_sample_filesystem", configuration.DatabaseName);
        Assert.Contains("/nestedset_sample_filesystem", configuration.Description, StringComparison.Ordinal);
    }

    /// <summary>Foreign or differently spelled database names cannot become a reset target.</summary>
    /// <param name="database">The database supplied by an adversarial endpoint.</param>
    [Theory]
    [InlineData("application")]
    [InlineData("nestedset_sample_kpis")]
    [InlineData("NESTEDSET_SAMPLE_FILESYSTEM")]
    public void ForeignDatabaseOverrideIsRejected(
        string database
    )
    {
        // Arrange
        var endpoint = ServerEndpoint + $"Database={database};";

        // Act
        var error = Record.Exception(() =>
            SampleDatabaseConfiguration.Create("filesystem", SampleProvider.MariaDb, endpoint));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.Contains("Database=nestedset_sample_filesystem", error.Message, StringComparison.Ordinal);
    }

    /// <summary>SQLite stores results in a fixed sample filename and enables relational foreign keys.</summary>
    /// <param name="sample">The known sample project.</param>
    /// <param name="database">The independently expected owned database.</param>
    [Theory]
    [InlineData("filesystem", "nestedset_sample_filesystem")]
    [InlineData("kpis", "nestedset_sample_kpis")]
    [InlineData("usergroups", "nestedset_sample_usergroups")]
    public async Task SqliteUsesOnlyTheOwnedFilename(
        string sample,
        string database
    )
    {
        // Arrange
        var directory = Path.Combine(Path.GetTempPath(), "nestedset-sample-configuration");
        var expectedPath = Path.Combine(Path.GetFullPath(directory), database + ".sqlite");

        // Act
        var configuration = SampleDatabaseConfiguration.Create(
            sample,
            SampleProvider.Sqlite,
            ServerEndpoint,
            directory);

        // Assert
        await using var context = new SampleLifecycleContext(configuration.CreateOptions<SampleLifecycleContext>());
        var connection = new SqliteConnectionStringBuilder(
            context.Database.GetDbConnection().ConnectionString);

        Assert.Equal(database, configuration.DatabaseName);
        Assert.Equal(expectedPath, configuration.SqlitePath);
        Assert.Equal(expectedPath, connection.DataSource);
        Assert.True(connection.ForeignKeys);
        Assert.Equal(SqliteOpenMode.ReadWriteCreate, connection.Mode);
    }

    /// <summary>Inspection options prevent SQLite from creating or writing the fixed sample file.</summary>
    [Fact]
    public async Task SqliteInspectionOptionsOpenOnlyAnExistingReadOnlyFile()
    {
        // Arrange
        var directory = Path.Combine(Path.GetTempPath(), "nestedset-sample-configuration");
        var configuration = SampleDatabaseConfiguration.Create(
            "filesystem",
            SampleProvider.Sqlite,
            ServerEndpoint,
            directory);

        // Act
        var options = configuration.CreateOptions<SampleLifecycleContext>(readOnly: true);

        // Assert
        await using var context = new SampleLifecycleContext(options);
        var connection = new SqliteConnectionStringBuilder(
            context.Database.GetDbConnection().ConnectionString);

        Assert.Equal(SqliteOpenMode.ReadOnly, connection.Mode);
        Assert.Equal(configuration.SqlitePath, connection.DataSource);
    }

    /// <summary>Unknown sample names cannot choose a directory or database through naming tricks.</summary>
    /// <param name="sample">The unrecognized project name.</param>
    [Theory]
    [InlineData("")]
    [InlineData("Filesystem")]
    [InlineData("../filesystem")]
    [InlineData("application")]
    public void UnknownSampleIsRejected(
        string sample
    )
    {
        // Arrange
        var directory = Path.Combine(Path.GetTempPath(), "nestedset-sample-configuration");

        // Act
        var error = Record.Exception(() =>
            SampleDatabaseConfiguration.Create(sample, SampleProvider.Sqlite, ServerEndpoint, directory));

        // Assert
        var invalid = Assert.IsType<ArgumentException>(error);
        Assert.Equal("sampleName", invalid.ParamName);
    }

    /// <summary>An undefined provider value fails before any connection is created.</summary>
    [Fact]
    public void UndefinedProviderIsRejected()
    {
        // Arrange
        var provider = (SampleProvider)int.MaxValue;

        // Act
        var error = Record.Exception(() => SampleDatabaseConfiguration.Create("filesystem", provider, ServerEndpoint));

        // Assert
        var invalid = Assert.IsType<ArgumentException>(error);
        Assert.Equal("provider", invalid.ParamName);
    }
}

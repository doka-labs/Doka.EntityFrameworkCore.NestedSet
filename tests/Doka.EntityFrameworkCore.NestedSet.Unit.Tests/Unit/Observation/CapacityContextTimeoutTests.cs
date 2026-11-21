using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks that functional capacity has no SQL latency gate and ordinary contexts remain unchanged.</summary>
public sealed class CapacityContextTimeoutTests
{
    /// <summary>The migration fixture's retained SQLite connection keeps its schema while limits are removed.</summary>
    [Fact]
    public async Task Configure_OpenSqliteConnection_RetainsDatabaseAndRemovesCommandLimit()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:;Default Timeout=17");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var services = OwnedModelTestServices<DbContext>.Create(
            new DbContextOptionsBuilder<DbContext>().UseSqlite(connection).Options);

        await using var context = new DbContext(services.Options);
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE RetainedSchema (Id INTEGER PRIMARY KEY)",
            TestContext.Current.CancellationToken);

        // Act
        FunctionalTestContextPolicy.Configure(context);
        var tables = await context.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value FROM sqlite_master WHERE name = 'RetainedSchema'")
            .SingleAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, tables);
        Assert.Equal(ConnectionState.Open, connection.State);
        Assert.Equal(0, connection.DefaultTimeout);
        Assert.Equal(0, context.Database.GetCommandTimeout());
    }

    /// <summary>Nested capacity profiles receive the same command policy with or without their own observers.</summary>
    /// <param name="observed">Whether the nested context has multiple independent command observers.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_NestedCapacityProfile_RemovesInheritedTimeouts(
        bool observed
    )
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(
            "Sqlite",
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await using var context = observed
            ? database.CreateContext(new EnterpriseProbe(), new CompositionCommandProbe())
            : database.CreateContext();

        using var command = context.Set<TreeNode>().Take(1).CreateDbCommand();
        using var native = context.Database.GetDbConnection().CreateCommand();

        // Assert
        Assert.Equal(0, command.CommandTimeout);
        Assert.Equal(0, native.CommandTimeout);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>
    /// Capacity commands and native transaction defaults do not impose an implicit performance threshold.
    /// </summary>
    /// <param name="engine">The supported native provider profile.</param>
    /// <param name="configured">The former EF timeout, including an inherited default or explicit zero.</param>
    [Theory]
    [InlineData("MySql", null)]
    [InlineData("MySql", 7)]
    [InlineData("MySql", 0)]
    [InlineData("MariaDb", null)]
    [InlineData("MariaDb", 7)]
    [InlineData("MariaDb", 0)]
    [InlineData("PostgreSql", null)]
    [InlineData("PostgreSql", 7)]
    [InlineData("PostgreSql", 0)]
    [InlineData("SqlServer", null)]
    [InlineData("SqlServer", 7)]
    [InlineData("SqlServer", 0)]
    [InlineData("Sqlite", null)]
    [InlineData("Sqlite", 7)]
    [InlineData("Sqlite", 0)]
    public void Configure_CapacityCommands_HaveNoImplicitLatencyLimit(
        string engine,
        int? configured
    )
    {
        // Arrange
        using var services = CreateServices(engine);
        using var context = new TreeContext(services.Options);
        context.Database.SetCommandTimeout(configured);

        // Act
        FunctionalTestContextPolicy.Configure(context);
        using var command = context
            .Set<TreeNode>()
            .Take(1)
            .CreateDbCommand();

        // Assert
        var connection = context.Database.GetDbConnection();
        var core = Assert.IsType<CoreOptionsExtension>(services.Options.FindExtension<CoreOptionsExtension>());
        Assert.NotNull(core.InternalServiceProvider);
        Assert.Equal(WarningBehavior.Throw, core.WarningsConfiguration.GetBehavior(
            CoreEventId.ManyServiceProvidersCreatedWarning));

        Assert.Equal(0, context.Database.GetCommandTimeout());
        Assert.Equal(0, command.CommandTimeout);
        Assert.Equal(ConnectionState.Closed, connection.State);
        if (connection is SqlConnection)
        {
            var options = new SqlConnectionStringBuilder(connection.ConnectionString);
            Assert.Equal(0, options.ConnectTimeout);
        }
        else
        {
            using var native = connection.CreateCommand();
            Assert.Equal(0, native.CommandTimeout);
        }
    }

    /// <summary>Other contexts sharing the original options retain their own unchanged connection.</summary>
    /// <param name="engine">The supported native provider profile.</param>
    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    [InlineData("Sqlite")]
    public void Configure_OrdinaryContext_RetainsOriginalTimeouts(
        string engine
    )
    {
        // Arrange
        using var services = CreateServices(engine);
        var options = services.Options;
        using var capacity = new TreeContext(options);
        using var ordinary = new TreeContext(options);
        ordinary.Database.SetCommandTimeout(7);
        var original = ordinary.Database.GetDbConnection()
            .ConnectionString;

        // Act
        FunctionalTestContextPolicy.Configure(capacity);
        using var command = ordinary
            .Set<TreeNode>()
            .Take(1)
            .CreateDbCommand();

        // Assert
        var core = Assert.IsType<CoreOptionsExtension>(options.FindExtension<CoreOptionsExtension>());
        Assert.NotNull(core.InternalServiceProvider);
        Assert.Equal(WarningBehavior.Throw, core.WarningsConfiguration.GetBehavior(
            CoreEventId.ManyServiceProvidersCreatedWarning));

        Assert.Equal(
            original,
            ordinary.Database.GetDbConnection()
                .ConnectionString);

        Assert.Equal(7, ordinary.Database.GetCommandTimeout());
        Assert.Equal(7, command.CommandTimeout);
        Assert.Equal(
            ConnectionState.Closed,
            ordinary.Database.GetDbConnection()
                .State);

        Assert.NotEqual(
            original,
            capacity.Database.GetDbConnection()
                .ConnectionString);
    }

    /// <summary>Uses nondefault native limits to expose latency gates and cross-context changes.</summary>
    private static OwnedModelTestServices<TreeContext> CreateServices(
        string engine
    )
    {
        var builder = new DbContextOptionsBuilder<TreeContext>();
        switch (engine)
        {
            case "MySql":
            case "MariaDb":
                builder.UseMySql(
                    "Server=localhost;Database=capacity_timeouts;Default Command Timeout=17",
                    engine == "MySql"
                        ? DatabaseTestTargets.MySql
                        : DatabaseTestTargets.MariaDb,
                    provider => provider.UseNestedSets());

                break;
            case "Sqlite":
                builder.UseSqlite("Data Source=:memory:;Default Timeout=17", provider => provider.UseNestedSets());

                break;
            case "PostgreSql":
                builder.UseNpgsql(
                    "Host=localhost;Database=capacity_timeouts;Command Timeout=17",
                    provider => provider.UseNestedSets());

                break;
            case "SqlServer":
                builder.UseSqlServer(
                    "Server=localhost;Database=capacity_timeouts;Connect Timeout=17",
                    provider => provider.UseNestedSets());

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(engine));
        }

        // WHY: Disabling EF caching still checks the process-global provider count. Own these intentional
        // provider variants explicitly so unrelated test order cannot trip that application warning.
        return OwnedModelTestServices<TreeContext>.Create(builder.Options);
    }
}

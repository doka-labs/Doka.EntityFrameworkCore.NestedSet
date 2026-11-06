using Microsoft.Data.SqlClient;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks that functional capacity has no SQL latency gate and ordinary contexts remain unchanged.</summary>
public sealed class CapacityContextTimeoutTests
{
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
    public void CapacityCommandsHaveNoImplicitLatencyLimit(
        string engine,
        int? configured
    )
    {
        // Arrange
        using var context = new TreeContext(Options(engine));
        context.Database.SetCommandTimeout(configured);

        // Act
        CapacityContextTimeouts.Configure(context);
        using var command = context
            .Set<TreeNode>()
            .Take(1)
            .CreateDbCommand();

        // Assert
        var connection = context.Database.GetDbConnection();
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
    public void OrdinaryContextRetainsOriginalTimeouts(
        string engine
    )
    {
        // Arrange
        var options = Options(engine);
        using var capacity = new TreeContext(options);
        using var ordinary = new TreeContext(options);
        ordinary.Database.SetCommandTimeout(7);
        var original = ordinary.Database.GetDbConnection()
            .ConnectionString;

        // Act
        CapacityContextTimeouts.Configure(capacity);
        using var command = ordinary
            .Set<TreeNode>()
            .Take(1)
            .CreateDbCommand();

        // Assert
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
    private static DbContextOptions<TreeContext> Options(
        string engine
    )
    {
        var builder = new DbContextOptionsBuilder<TreeContext>().EnableServiceProviderCaching(false);
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

        return builder.Options;
    }
}

using System.Data;
using System.Data.Common;
using Doka.EntityFrameworkCore.MySql;
using Doka.EntityFrameworkCore.NestedSet.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MySqlConnector;
using Npgsql;
using ProviderGuidFormat = Doka.EntityFrameworkCore.MySql.MySqlGuidFormat;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>
/// Qualifies observational timeout defaults without connecting or weakening explicit provider settings.
/// </summary>
public sealed class BenchmarkProviderTimeoutTests
{
    /// <summary>Every real benchmark provider retains an unlimited EF command policy through options cloning.</summary>
    /// <param name="engine">The provider configured by the shared benchmark entry.</param>
    [Theory]
    [InlineData(BenchmarkEngine.MySql)]
    [InlineData(BenchmarkEngine.MariaDb)]
    [InlineData(BenchmarkEngine.PostgreSql)]
    [InlineData(BenchmarkEngine.SqlServer)]
    [InlineData(BenchmarkEngine.SqliteMemory)]
    [InlineData(BenchmarkEngine.SqliteFile)]
    public void ConfigureProvider_DefaultOptions_HaveNoImplicitCommandDeadline(
        BenchmarkEngine engine
    )
    {
        // Arrange
        var builder = new DbContextOptionsBuilder();

        // Act
        BenchmarkEnvironment.ConfigureProvider(builder, engine, ConnectionString(engine));
        var copied = new DbContextOptionsBuilder(builder.Options).Options;
        using var context = new DbContext(copied);

        // Assert
        Assert.Equal(0, context.Database.GetCommandTimeout());
        Assert.Equal(0, RelationalOptionsExtension.Extract(copied).CommandTimeout);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Native commands also avoid a second implicit deadline outside EF command construction.</summary>
    /// <param name="engine">The provider whose own ADO command inherits the configured native default.</param>
    [Theory]
    [InlineData(BenchmarkEngine.MySql)]
    [InlineData(BenchmarkEngine.MariaDb)]
    [InlineData(BenchmarkEngine.PostgreSql)]
    [InlineData(BenchmarkEngine.SqlServer)]
    [InlineData(BenchmarkEngine.SqliteMemory)]
    [InlineData(BenchmarkEngine.SqliteFile)]
    public void ConfigureProvider_DefaultNativeCommand_HasNoImplicitDeadline(
        BenchmarkEngine engine
    )
    {
        // Arrange
        var builder = new DbContextOptionsBuilder();
        BenchmarkEnvironment.ConfigureProvider(builder, engine, ConnectionString(engine));
        using var context = new DbContext(builder.Options);

        // Act
        using var command = context.Database.GetDbConnection().CreateCommand();

        // Assert
        Assert.Equal(0, command.CommandTimeout);
        Assert.Equal(ConnectionState.Closed, command.Connection!.State);
    }

    /// <summary>Native SQL Server undo uses the connection deadline even when no EF command is involved.</summary>
    [Fact]
    public void ConfigureProvider_DefaultSqlServerConnection_HasNoNativeUndoDeadline()
    {
        // Arrange
        var builder = new DbContextOptionsBuilder();

        // Act
        BenchmarkEnvironment.ConfigureProvider(builder, BenchmarkEngine.SqlServer,
            ConnectionString(BenchmarkEngine.SqlServer));

        using var context = new DbContext(builder.Options);
        var connection = Assert.IsType<SqlConnection>(context.Database.GetDbConnection());

        // Assert
        Assert.Equal(0, connection.ConnectionTimeout);
        Assert.False(new SqlConnectionStringBuilder(connection.ConnectionString).Pooling);
    }

    /// <summary>
    /// An explicitly selected native timeout remains intentional rather than becoming a hidden default.
    /// </summary>
    /// <param name="engine">The provider with an explicit command timeout in its input connection string.</param>
    /// <param name="timeout">A deliberate value, including an explicitly retained provider default.</param>
    [Theory]
    [InlineData(BenchmarkEngine.MySql, 17)]
    [InlineData(BenchmarkEngine.MySql, 30)]
    [InlineData(BenchmarkEngine.MariaDb, 17)]
    [InlineData(BenchmarkEngine.MariaDb, 30)]
    [InlineData(BenchmarkEngine.PostgreSql, 17)]
    [InlineData(BenchmarkEngine.PostgreSql, 30)]
    [InlineData(BenchmarkEngine.SqlServer, 17)]
    [InlineData(BenchmarkEngine.SqlServer, 30)]
    [InlineData(BenchmarkEngine.SqliteMemory, 17)]
    [InlineData(BenchmarkEngine.SqliteMemory, 30)]
    [InlineData(BenchmarkEngine.SqliteFile, 17)]
    [InlineData(BenchmarkEngine.SqliteFile, 30)]
    public void ConfigureProvider_ExplicitNativeTimeout_PreservesIntentionalOverride(
        BenchmarkEngine engine,
        int timeout
    )
    {
        // Arrange
        var native = new DbConnectionStringBuilder { ConnectionString = ConnectionString(engine) };
        native[TimeoutKeyword(engine)] = timeout;
        var builder = new DbContextOptionsBuilder();

        // Act
        BenchmarkEnvironment.ConfigureProvider(builder, engine, native.ConnectionString);
        using var context = new DbContext(builder.Options);
        using var command = context.Database.GetDbConnection().CreateCommand();

        // Assert
        Assert.Equal(timeout, command.CommandTimeout);
        Assert.Equal(timeout, context.Database.GetCommandTimeout());
        Assert.Equal(ConnectionState.Closed, command.Connection!.State);
    }

    /// <summary>
    /// Explicit relational settings survive the shared provider registration and immutable options copy.
    /// </summary>
    /// <param name="engine">The provider whose previously configured relational extension must be retained.</param>
    [Theory]
    [InlineData(BenchmarkEngine.MySql)]
    [InlineData(BenchmarkEngine.MariaDb)]
    [InlineData(BenchmarkEngine.PostgreSql)]
    [InlineData(BenchmarkEngine.SqlServer)]
    [InlineData(BenchmarkEngine.SqliteMemory)]
    [InlineData(BenchmarkEngine.SqliteFile)]
    public void ConfigureProvider_ExplicitRelationalOptions_PreservesTimeoutAndOtherSettings(
        BenchmarkEngine engine
    )
    {
        // Arrange
        var builder = new DbContextOptionsBuilder().UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        BenchmarkEnvironment.ConfigureProvider(builder, engine, ConnectionString(engine));
        var original = RelationalOptionsExtension.Extract(builder.Options)
            .WithCommandTimeout(17)
            .WithMaxBatchSize(29)
            .WithUseRelationalNulls(true);

        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(original);

        // Act
        BenchmarkEnvironment.ConfigureProvider(builder, engine, ConnectionString(engine));
        var configured = RelationalOptionsExtension.Extract(builder.Options);
        using var context = new DbContext(builder.Options);

        // Assert
        Assert.Equal(original.GetType(), configured.GetType());
        Assert.Equal(17, context.Database.GetCommandTimeout());
        Assert.Equal(29, configured.MaxBatchSize);
        Assert.True(configured.UseRelationalNulls);
        Assert.Equal(QueryTrackingBehavior.NoTracking, context.ChangeTracker.QueryTrackingBehavior);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Native normalization preserves every unrelated, explicitly configured connection property.</summary>
    /// <param name="engine">The provider whose connection settings are retained by default normalization.</param>
    [Theory]
    [InlineData(BenchmarkEngine.MySql)]
    [InlineData(BenchmarkEngine.MariaDb)]
    [InlineData(BenchmarkEngine.PostgreSql)]
    [InlineData(BenchmarkEngine.SqlServer)]
    [InlineData(BenchmarkEngine.SqliteMemory)]
    [InlineData(BenchmarkEngine.SqliteFile)]
    public void ConfigureProvider_NativeDefaults_PreservesUnrelatedConnectionProperties(
        BenchmarkEngine engine
    )
    {
        // Arrange
        var original = NativeOptions(engine, ConnectionString(engine));
        var expected = new DbConnectionStringBuilder { ConnectionString = original.ConnectionString };
        var builder = new DbContextOptionsBuilder();

        // Act
        BenchmarkEnvironment.ConfigureProvider(builder, engine, original.ConnectionString);
        using var context = new DbContext(builder.Options);
        var actual = new DbConnectionStringBuilder
        {
            ConnectionString = context.Database.GetDbConnection().ConnectionString,
        };

        // Assert
        foreach (string keyword in expected.Keys)
        {
            Assert.Equal(expected[keyword], actual[keyword]);
        }

        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>
    /// Intentional per-context overrides remain available after the observational default is installed.
    /// </summary>
    /// <param name="engine">The provider whose ordinary EF override must remain effective.</param>
    [Theory]
    [InlineData(BenchmarkEngine.MySql)]
    [InlineData(BenchmarkEngine.MariaDb)]
    [InlineData(BenchmarkEngine.PostgreSql)]
    [InlineData(BenchmarkEngine.SqlServer)]
    [InlineData(BenchmarkEngine.SqliteMemory)]
    [InlineData(BenchmarkEngine.SqliteFile)]
    public void ConfigureProvider_LaterContextTimeout_PreservesIntentionalOverride(
        BenchmarkEngine engine
    )
    {
        // Arrange
        var builder = new DbContextOptionsBuilder();
        BenchmarkEnvironment.ConfigureProvider(builder, engine, ConnectionString(engine));
        using var context = new DbContext(builder.Options);

        // Act
        context.Database.SetCommandTimeout(23);

        // Assert
        Assert.Equal(23, context.Database.GetCommandTimeout());
        Assert.Equal(0, RelationalOptionsExtension.Extract(builder.Options).CommandTimeout);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>A deliberate connection deadline is not replaced by the observational undo default.</summary>
    [Fact]
    public void ConfigureProvider_ExplicitSqlServerConnectionTimeout_PreservesIntentionalOverride()
    {
        // Arrange
        var native = new SqlConnectionStringBuilder(ConnectionString(BenchmarkEngine.SqlServer))
        {
            ConnectTimeout = 19,
        };

        var builder = new DbContextOptionsBuilder();

        // Act
        BenchmarkEnvironment.ConfigureProvider(builder, BenchmarkEngine.SqlServer, native.ConnectionString);
        using var context = new DbContext(builder.Options);

        // Assert
        Assert.Equal(19, context.Database.GetDbConnection().ConnectionTimeout);
        Assert.Equal(0, context.Database.GetCommandTimeout());
    }

    /// <summary>The timeout policy does not replace Doka's configured native GUID representation.</summary>
    /// <param name="engine">The explicitly configured MySQL or MariaDB server profile.</param>
    [Theory]
    [InlineData(BenchmarkEngine.MySql)]
    [InlineData(BenchmarkEngine.MariaDb)]
    public void ConfigureProvider_ExplicitGuidStorage_PreservesDokaMapping(
        BenchmarkEngine engine
    )
    {
        // Arrange
        var builder = new DbContextOptionsBuilder().UseMySql(
            ConnectionString(engine),
            engine == BenchmarkEngine.MySql ? DatabaseTestTargets.MySql : DatabaseTestTargets.MariaDb,
            provider => provider.DefaultGuidFormat(ProviderGuidFormat.Char36).UseNestedSets());

        // Act
        BenchmarkEnvironment.ConfigureProvider(builder, engine, ConnectionString(engine));
        using var context = new ManualBenchmarkContext(builder.Options);
        var mapping = context.Model.FindEntityType(typeof(BenchmarkNode))!
            .FindProperty(nameof(BenchmarkNode.TreeId))!
            .GetRelationalTypeMapping();

        // Assert
        Assert.Equal("char(36)", mapping.StoreType);
        Assert.Equal(0, context.Database.GetCommandTimeout());
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Invalid engines reject before introducing a relational extension or changing caller options.</summary>
    [Fact]
    public void ConfigureProvider_UnknownEngine_RejectsWithoutMutatingOptions()
    {
        // Arrange
        var builder = new DbContextOptionsBuilder();
        var original = builder.Options;

        // Act
        var failure = Record.Exception(() => BenchmarkEnvironment.ConfigureProvider(builder,
            (BenchmarkEngine)int.MaxValue, "invalid-engine-connection"));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(failure);
        Assert.Same(original, builder.Options);
        Assert.Empty(builder.Options.Extensions);
    }

    /// <summary>Uses closed, nonpooled fixture endpoints; no test connects to these synthetic servers.</summary>
    private static string ConnectionString(
        BenchmarkEngine engine
    ) => engine switch
    {
        BenchmarkEngine.MySql or BenchmarkEngine.MariaDb =>
            "Server=localhost;Database=timeout_contract;User ID=fixture;Password=fixture;"
            + "Pooling=false;AutoEnlist=false",
        BenchmarkEngine.PostgreSql =>
            "Host=localhost;Database=timeout_contract;Username=fixture;Password=fixture;Pooling=false;Timeout=19",
        BenchmarkEngine.SqlServer =>
            "Server=localhost;Database=timeout_contract;User ID=fixture;Password=fixture;Pooling=false",
        BenchmarkEngine.SqliteMemory or BenchmarkEngine.SqliteFile =>
            "Data Source=:memory:;Pooling=false;Foreign Keys=true",
        _ => throw new ArgumentOutOfRangeException(nameof(engine)),
    };

    /// <summary>Canonicalizes the fixture options using the actual provider instead of comparing raw aliases.</summary>
    private static DbConnectionStringBuilder NativeOptions(
        BenchmarkEngine engine,
        string connectionString
    ) => engine switch
    {
        BenchmarkEngine.MySql or BenchmarkEngine.MariaDb => new MySqlConnectionStringBuilder(connectionString),
        BenchmarkEngine.PostgreSql => new NpgsqlConnectionStringBuilder(connectionString),
        BenchmarkEngine.SqlServer => new SqlConnectionStringBuilder(connectionString),
        BenchmarkEngine.SqliteMemory or BenchmarkEngine.SqliteFile =>
            new SqliteConnectionStringBuilder(connectionString),
        _ => throw new ArgumentOutOfRangeException(nameof(engine)),
    };

    /// <summary>Identifies each ADO provider's actual command-default connection option.</summary>
    private static string TimeoutKeyword(
        BenchmarkEngine engine
    ) => engine switch
    {
        BenchmarkEngine.MySql or BenchmarkEngine.MariaDb => "Default Command Timeout",
        BenchmarkEngine.PostgreSql or BenchmarkEngine.SqlServer => "Command Timeout",
        BenchmarkEngine.SqliteMemory or BenchmarkEngine.SqliteFile => "Default Timeout",
        _ => throw new ArgumentOutOfRangeException(nameof(engine)),
    };
}

using Microsoft.EntityFrameworkCore.Storage;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ProviderCapabilitiesTests
{
    /// <summary>
    /// Concrete adapters preserve native collation grammar without opening a connection or creating commands.
    /// </summary>
    /// <param name="engine">The actual adapter and relational SQL helper under test.</param>
    /// <param name="collation">A model collation, including native identifier delimiters in escaping cases.</param>
    /// <param name="expected">The exact escaped identifier or validated SQL Server token.</param>
    [Theory]
    [InlineData("MySql", "utf8mb4_bin", "`utf8mb4_bin`")]
    [InlineData("MySql", "name`); DROP TABLE trees; --", "`name``); DROP TABLE trees; --`")]
    [InlineData("MariaDb", "utf8mb4_bin", "`utf8mb4_bin`")]
    [InlineData("MariaDb", "name`); DROP TABLE trees; --", "`name``); DROP TABLE trees; --`")]
    [InlineData("PostgreSql", "C", "\"C\"")]
    [InlineData("PostgreSql", "name\"); DROP TABLE trees; --", "\"name\"\"); DROP TABLE trees; --\"")]
    [InlineData("Sqlite", "BINARY", "\"BINARY\"")]
    [InlineData("Sqlite", "name\"); DROP TABLE trees; --", "\"name\"\"); DROP TABLE trees; --\"")]
    [InlineData("SqlServer", "Latin1_General_100_BIN2", "Latin1_General_100_BIN2")]
    [InlineData("SqlServer", "SQL_Latin1_General_CP1_CI_AS", "SQL_Latin1_General_CP1_CI_AS")]
    public void CollationRenderingPreservesNativeGrammarWithoutIo(
        string engine,
        string collation,
        string expected
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var owner = OwnedModelTestServices<CompositionGuardContext>.Create(CollationOptions(engine, boundary));
        using var context = new CompositionGuardContext(owner.Options);
        var sql = context.GetService<ISqlGenerationHelper>();
        var adapter = NestedSetTestAdapters.ForEngine(engine);

        // Act
        var rendered = adapter.CollationSql(sql, collation);

        // Assert
        Assert.Equal(expected, rendered);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>
    /// SQL Server rejects empty, quoted, non-ASCII, and injected bare collation tokens before database I/O.
    /// </summary>
    /// <param name="collation">A value outside SQL Server's supported bare-token grammar.</param>
    [Theory]
    [InlineData("")]
    [InlineData("Latin1 General")]
    [InlineData("[Latin1_General_100_BIN2]")]
    [InlineData("\"Latin1_General_100_BIN2\"")]
    [InlineData("Latin1_General_100_BIN2; DROP TABLE trees; --")]
    [InlineData("Latin1_General_100_BIN2\n")]
    [InlineData("Lat\u00edn1_General_100_BIN2")]
    [InlineData("Latin1_General_\uff11\uff10\uff10_BIN2")]
    public void SqlServerRejectsInvalidCollationTokensWithoutIo(
        string collation
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var owner = OwnedModelTestServices<CompositionGuardContext>.Create(
            CollationOptions("SqlServer", boundary));
        using var context = new CompositionGuardContext(owner.Options);
        var sql = context.GetService<ISqlGenerationHelper>();
        var adapter = NestedSetTestAdapters.ForEngine("SqlServer");

        // Act
        var error = Assert.Throws<InvalidOperationException>(() => adapter.CollationSql(sql, collation));

        // Assert
        Assert.Contains("ASCII letters, digits, and underscores", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>
    /// Uses the actual native SQL helper and fails immediately if metadata setup attempts database I/O.
    /// </summary>
    /// <param name="engine">The concrete provider whose configured helper is required.</param>
    /// <param name="boundary">The observer that rejects connection and command creation.</param>
    /// <returns>Actual provider options bound to the test's no-I/O observers.</returns>
    private static DbContextOptions<CompositionGuardContext> CollationOptions(
        string engine,
        CompositionIoBoundary boundary
    )
    {
        var connection = engine switch
        {
            "Sqlite" => "Data Source=:memory:",
            "PostgreSql" => "Host=127.0.0.1;Database=collation_contract;Username=unused;Pooling=false",
            _ => "Server=127.0.0.1;Database=collation_contract;User ID=unused;Password=unused;Pooling=false",
        };

        var options = new DbContextOptionsBuilder<CompositionGuardContext>(
                ModelCompatibilityDatabase.Options<CompositionGuardContext>(engine, connection))
            .AddInterceptors(boundary, boundary.Commands)
            .Options;

        return options;
    }
}

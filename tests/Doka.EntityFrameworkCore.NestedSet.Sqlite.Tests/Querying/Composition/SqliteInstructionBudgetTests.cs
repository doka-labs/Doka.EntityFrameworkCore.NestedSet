using Microsoft.Data.Sqlite;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Checks deterministic native read interruption on fresh connections without provider resources.</summary>
[DatabaseIndependent]
public sealed class SqliteInstructionBudgetTests : ProviderTest,
    IClassFixture<ProviderFixture<ProviderResources, SqliteEngine>>
{
    private const string FiniteRead = """
        WITH RECURSIVE numbers(Value) AS (
            SELECT 1 UNION ALL SELECT Value + 1 FROM numbers WHERE Value < 1000
        )
        SELECT SUM(Value) FROM numbers;
        """;

    private const string QuadraticRead = """
        WITH RECURSIVE numbers(Value) AS (
            SELECT 1 UNION ALL SELECT Value + 1 FROM numbers WHERE Value < 100
        )
        SELECT SUM(first.Value * second.Value) FROM numbers AS first CROSS JOIN numbers AS second;
        """;

    /// <summary>Retains explicit SQLite test ownership without starting shared database resources.</summary>
    /// <param name="fixture">The resource-free provider marker.</param>
    public SqliteInstructionBudgetTests(
        ProviderFixture<ProviderResources, SqliteEngine> fixture
    ) : base(fixture) { }

    /// <summary>A finite read returns its exact result and records positive work below its fixed allowance.</summary>
    [Fact]
    public async Task FiniteReadCompletesWithinInstructionBudget()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:;Pooling=False");
        await connection.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = FiniteRead;
        using var budget = new Specifications.SqliteInstructionBudget(connection, 1_000_000);

        // Act
        var result = await command.ExecuteScalarAsync(CancellationToken.None);

        // Assert
        Assert.Equal(500_500L, result);
        Assert.False(budget.Exceeded);
        Assert.InRange(budget.ObservedInstructions, Specifications.SqliteInstructionBudget.Quantum, 1_000_000);
    }

    /// <summary>A quadratic read is interrupted deterministically before it can overrun its fixed allowance.</summary>
    [Fact]
    public async Task QuadraticReadIsInterruptedByInstructionBudget()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:;Pooling=False");
        await connection.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = QuadraticRead;
        using var budget = new Specifications.SqliteInstructionBudget(connection, 10_000);

        // Act
        var error = await Record.ExceptionAsync(async () =>
        {
            await command.ExecuteScalarAsync(CancellationToken.None);
        });

        // Assert
        Assert.Equal(9, Assert.IsType<SqliteException>(error).SqliteErrorCode);
        Assert.True(budget.Exceeded);
        Assert.Equal(11_000, budget.ObservedInstructions);
    }

    /// <summary>Removing an interrupted read's handler lets the same native connection execute normally.</summary>
    [Fact]
    public async Task DisposingInterruptedBudgetRestoresConnectionReads()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:;Pooling=False");
        await connection.OpenAsync(CancellationToken.None);
        Exception? interruption;
        bool exceeded;
        using (var budget = new Specifications.SqliteInstructionBudget(connection, 10_000))
        {
            // WHY: The interrupted statement establishes the connection state required by this distinct
            // cleanup contract; the measured action below is the first read after removing its handler.
            await using var interrupted = connection.CreateCommand();
            interrupted.CommandText = QuadraticRead;
            interruption = await Record.ExceptionAsync(async () =>
            {
                await interrupted.ExecuteScalarAsync(CancellationToken.None);
            });
            exceeded = budget.Exceeded;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 42;";

        // Act
        var result = await command.ExecuteScalarAsync(CancellationToken.None);

        // Assert
        Assert.Equal(9, Assert.IsType<SqliteException>(interruption).SqliteErrorCode);
        Assert.True(exceeded);
        Assert.Equal(42L, result);
    }
}

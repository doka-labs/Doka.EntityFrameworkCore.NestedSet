namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Proves that the zero-I/O observers reject actual execution attempts.</summary>
public sealed class CompositionIoBoundaryTests
{
    /// <summary>The connection observer stops an actual asynchronous connection attempt.</summary>
    /// <returns>A task that completes after the deliberate connection failure is observed.</returns>
    [Fact]
    public async Task ConnectionObserverRejectsExecution()
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        await using var context = CompositionGuardContext.Create(boundary, boundary.Commands);

        // Act
        var error = await Record.ExceptionAsync(() => context.Database.OpenConnectionAsync(CancellationToken.None));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Equal(1, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>The command observer also stops execution when the caller already owns an open connection.</summary>
    /// <returns>A task that completes after the deliberate command failure is observed.</returns>
    [Fact]
    public async Task CommandObserverRejectsExecutionOnOpenConnection()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        var boundary = new CompositionIoBoundary();
        var options = new DbContextOptionsBuilder<CompositionGuardContext>()
            .UseSqlite(connection, provider => provider.UseNestedSets())
            .AddInterceptors(boundary, boundary.Commands)
            .Options;

        await using var context = new CompositionGuardContext(options);

        // Act
        var error = await Record.ExceptionAsync(() => context.Database.ExecuteSqlRawAsync(
            "SELECT 1", CancellationToken.None));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(1, boundary.Commands.Created);
    }
}

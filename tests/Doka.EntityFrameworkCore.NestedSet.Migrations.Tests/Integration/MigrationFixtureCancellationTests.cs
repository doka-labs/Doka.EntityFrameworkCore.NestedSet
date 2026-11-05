namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>Rejects canceled creation before any provider or container can acquire an owned resource.</summary>
public sealed class MigrationFixtureCancellationTests
{
    /// <summary>Preserves the caller's cancellation identity before selecting or starting a database engine.</summary>
    /// <param name="engine">The database engine that must remain unstarted.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task CanceledCreationRejectsBeforeAcquiringResources(
        string engine
    )
    {
        // Arrange
        await using var fixture = new MigrationFixture();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var error = await Record.ExceptionAsync(() => fixture.CreateDatabaseAsync(engine, cancellation.Token));

        // Assert
        var canceled = Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
    }
}

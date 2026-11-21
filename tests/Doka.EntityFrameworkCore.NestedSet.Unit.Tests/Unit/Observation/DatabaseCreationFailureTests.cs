namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Preserves failures before a functional database can transfer resource ownership.</summary>
public sealed class DatabaseCreationFailureTests
{
    /// <summary>
    /// An unsupported engine remains an argument rejection without constructing a providerless context.
    /// </summary>
    [Fact]
    public async Task CreateAsync_UnknownEngine_PreservesArgumentRejection()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var error = await Record.ExceptionAsync(() => TestDatabase.CreateAsync("Unknown", cancellationToken));

        // Assert
        Assert.Equal("engine", Assert.IsType<ArgumentOutOfRangeException>(error).ParamName);
    }

    /// <summary>The unit assembly's absent server fixture fails before any provider or database is owned.</summary>
    [Fact]
    public async Task CreateAsync_ServerFixtureUnavailable_PreservesStartupFailure()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var error = await Record.ExceptionAsync(() => TestDatabase.CreateAsync("MySql", cancellationToken));

        // Assert
        Assert.Equal(
            "The current test assembly has no database-server fixture.",
            Assert.IsType<InvalidOperationException>(error).Message);
    }

    /// <summary>Cancellation before provider startup retains the caller's exact canceled token.</summary>
    [Fact]
    public async Task CreateAsync_PreCanceledCaller_PreservesCancellationToken()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var error = await Record.ExceptionAsync(() => TestDatabase.CreateAsync("MySql", cancellation.Token));

        // Assert
        Assert.Equal(cancellation.Token, Assert.IsType<OperationCanceledException>(error).CancellationToken);
    }
}

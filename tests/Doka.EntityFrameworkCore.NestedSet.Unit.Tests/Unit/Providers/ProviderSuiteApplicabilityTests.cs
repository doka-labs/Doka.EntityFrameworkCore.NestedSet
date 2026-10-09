namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Prevents missing supported wrappers and IDE-visible wholly excluded test families.</summary>
public sealed class ProviderSuiteApplicabilityTests
{
    /// <summary>The common writer contract requires exactly one wrapper on every engine.</summary>
    /// <param name="engine">The engine whose shared wrapper is required.</param>
    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    [InlineData("Sqlite")]
    public void OrdinaryFamilyRequiresEveryEngineWrapper(
        string engine
    )
    {
        // Arrange
        var suite = typeof(ConcurrentWriterTests);

        // Act
        var required = ProviderTestContract.RequiresSharedWrapper(suite, engine);

        // Assert
        Assert.True(required);
    }

    /// <summary>Server-only scenarios require all server wrappers and reject a SQLite wrapper.</summary>
    /// <param name="engine">The engine whose method-level applicability is inspected.</param>
    /// <param name="expected">Whether the engine can execute at least one declared scenario.</param>
    [Theory]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", true)]
    [InlineData("PostgreSql", true)]
    [InlineData("SqlServer", true)]
    [InlineData("Sqlite", false)]
    public void FullyExcludedFamilyHasNoEngineWrapper(
        string engine,
        bool expected
    )
    {
        // Arrange
        var suite = typeof(ConcurrentCapacityTests);

        // Act
        var required = ProviderTestContract.RequiresSharedWrapper(suite, engine);

        // Assert
        Assert.Equal(expected, required);
    }

    /// <summary>An excluded method cannot remove a wrapper needed by other ordinary methods.</summary>
    [Fact]
    public void PartiallyExcludedFamilyStillRequiresItsWrapper()
    {
        // Arrange
        var suite = typeof(ProviderAnnotationTests);

        // Act
        var required = ProviderTestContract.RequiresSharedWrapper(suite, "MariaDb");

        // Assert
        Assert.True(required);
    }

    /// <summary>A helper without test declarations cannot require an executable provider wrapper.</summary>
    [Fact]
    public void NonTestHelperDoesNotRequireWrapper()
    {
        // Arrange
        var suite = typeof(ConcurrentCapacityTestSupport);

        // Act
        var required = ProviderTestContract.RequiresSharedWrapper(suite, "SqlServer");

        // Assert
        Assert.False(required);
    }

    /// <summary>An unknown engine must fail rather than silently appear to require coverage.</summary>
    [Fact]
    public void UnknownEngineIsRejected()
    {
        // Arrange
        var suite = typeof(ConcurrentWriterTests);

        // Act
        var error = Record.Exception(() => ProviderTestContract.RequiresSharedWrapper(suite, "Postgres"));

        // Assert
        var invalid = Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("Unknown integration test provider", invalid.Message, StringComparison.Ordinal);
    }
}

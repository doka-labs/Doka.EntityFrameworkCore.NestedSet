namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>Verifies mixed migration rows and explicitly bound tests without starting database resources.</summary>
public sealed class DatabasePlatformTests
{
    /// <summary>The named engine argument is selected by position, not by method name or another string value.</summary>
    [Theory]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("Sqlite")]
    public void MixedRowUsesItsEngineArgument(
        string engine
    )
    {
        // Arrange
        var attribute = new DatabasePlatformAttribute();
        var method = typeof(DatabasePlatformTests).GetMethod(nameof(MixedRowProbe), BindingFlags.NonPublic | BindingFlags.Static)!;

        // Act
        var resolved = attribute.ResolveEngine(method, ["SqlServer", engine]);

        // Assert
        Assert.Equal(engine, resolved);
    }

    /// <summary>An explicit SQL-only hook works when the theory has no engine parameter.</summary>
    [Fact]
    public void ExplicitEngineDoesNotRequireEngineArgument()
    {
        // Arrange
        var attribute = new DatabasePlatformAttribute("SqlServer");
        var method = typeof(DatabasePlatformTests).GetMethod(nameof(ExplicitEngineDoesNotRequireEngineArgument))!;

        // Act
        var resolved = attribute.ResolveEngine(method, []);

        // Assert
        Assert.Equal("SqlServer", resolved);
    }

    /// <summary>Metadata-only tests are not classified from provider words in their names or inputs.</summary>
    [Fact]
    public void MetadataDoesNotRequireDatabase()
    {
        // Arrange
        var attribute = new DatabasePlatformAttribute();
        var method = typeof(DatabasePlatformTests).GetMethod(nameof(ExplicitEngineDoesNotRequireEngineArgument))!;

        // Act
        var resolved = attribute.ResolveEngine(method, []);

        // Assert
        Assert.Null(resolved);
    }

    /// <summary>A missing row engine fails instead of silently bypassing platform applicability.</summary>
    [Fact]
    public void MissingEngineFailsClosed()
    {
        // Arrange
        var attribute = new DatabasePlatformAttribute();
        var method = typeof(DatabasePlatformTests).GetMethod(nameof(MixedRowProbe), BindingFlags.NonPublic | BindingFlags.Static)!;

        // Act
        var exception = Record.Exception(() => attribute.ResolveEngine(method, ["SqlServer", null]));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    /// <summary>Provides reflection metadata where an unrelated string precedes the engine.</summary>
    private static void MixedRowProbe(
        string payload,
        string engine
    ) { }
}

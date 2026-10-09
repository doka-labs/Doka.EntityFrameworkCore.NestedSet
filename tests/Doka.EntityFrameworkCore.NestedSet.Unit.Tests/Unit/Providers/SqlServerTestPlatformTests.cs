using System.Runtime.InteropServices;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies native execution, visible local skips, and fail-closed hosted SQL Server coverage.</summary>
public sealed class SqlServerTestPlatformTests
{
    /// <summary>Native x64 hosts execute SQL Server in local and hosted runs.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeX64ExecutesSqlServer(
        bool continuousIntegration
    )
    {
        // Arrange
        var architecture = Architecture.X64;

        // Act
        var exception = Record.Exception(() =>
            SqlServerTestPlatform.BeforeDatabaseTest("SqlServer", architecture, continuousIntegration));

        // Assert
        Assert.Null(exception);
    }

    /// <summary>Unsupported local hosts produce a visible xUnit skip with the platform reason.</summary>
    [Theory]
    [InlineData(Architecture.Arm64)]
    [InlineData(Architecture.Arm)]
    [InlineData(Architecture.X86)]
    public void UnsupportedLocalHostSkipsSqlServer(
        Architecture architecture
    )
    {
        // Arrange
        const bool continuousIntegration = false;
        SkipException? exception = null;

        // Act
        try
        {
            SqlServerTestPlatform.BeforeDatabaseTest("SqlServer", architecture, continuousIntegration);
        }
        catch (SkipException skipped)
        {
            // WHY: Record.Exception deliberately rethrows dynamic skips; this test verifies the skip itself.
            exception = skipped;
        }

        // Assert
        Assert.NotNull(exception);
        Assert.Contains(architecture.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains("native x64 CI", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Unsupported hosted runners fail instead of silently losing SQL Server qualification.</summary>
    [Theory]
    [InlineData(Architecture.Arm64)]
    [InlineData(Architecture.Arm)]
    [InlineData(Architecture.X86)]
    public void UnsupportedCiHostFailsSqlServer(
        Architecture architecture
    )
    {
        // Arrange
        const bool continuousIntegration = true;

        // Act
        var exception = Record.Exception(() =>
            SqlServerTestPlatform.BeforeDatabaseTest("SqlServer", architecture, continuousIntegration));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains(architecture.ToString(), exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Other databases and metadata-only tests are unaffected by the SQL Server host boundary.</summary>
    [Theory]
    [InlineData("MySql", false)]
    [InlineData("MariaDb", false)]
    [InlineData("PostgreSql", false)]
    [InlineData("Sqlite", false)]
    [InlineData(null, false)]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", true)]
    [InlineData("PostgreSql", true)]
    [InlineData("Sqlite", true)]
    [InlineData(null, true)]
    public void OtherEnginesAndMetadataRemainRunnable(
        string? engine,
        bool continuousIntegration
    )
    {
        // Arrange
        var architecture = Architecture.Arm64;

        // Act
        var exception = Record.Exception(() =>
            SqlServerTestPlatform.BeforeDatabaseTest(engine, architecture, continuousIntegration));

        // Assert
        Assert.Null(exception);
    }
}

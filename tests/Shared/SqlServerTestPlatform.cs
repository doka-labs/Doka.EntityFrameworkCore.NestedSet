using System.Runtime.InteropServices;

namespace Doka.EntityFrameworkCore.NestedSet.Testing;

/// <summary>Separates unsupported local SQL Server hosts from mandatory native CI execution.</summary>
internal static class SqlServerTestPlatform
{
    /// <summary>Checks applicability before a test body can start a SQL Server container.</summary>
    internal static void BeforeDatabaseTest(
        string? engine
    ) => BeforeDatabaseTest(engine, RuntimeInformation.OSArchitecture, IsContinuousIntegration());

    /// <summary>Evaluates the same platform contract without changing the process environment.</summary>
    internal static void BeforeDatabaseTest(
        string? engine,
        Architecture architecture,
        bool continuousIntegration
    )
    {
        if (engine != "SqlServer" || architecture == Architecture.X64)
        {
            return;
        }

        var reason = $"SQL Server database tests require native x64; this host is {architecture}. "
            + "Microsoft does not support SQL Server containers under architecture emulation. "
            + "The complete SQL Server suite remains mandatory on native x64 CI.";

        // WHY: A misconfigured CI runner must fail instead of producing a green run without SQL Server coverage.
        if (continuousIntegration)
        {
            throw new InvalidOperationException(reason);
        }

        Assert.Skip(reason);
    }

    /// <summary>Rejects a missing test hook before the container builder or server cache is touched.</summary>
    internal static void RequireSupportedContainerHost(
        string engine
    )
    {
        // WHY: This is an infrastructure failure, not a dynamic skip that an expected-exception test could catch.
        if (engine == "SqlServer" && RuntimeInformation.OSArchitecture != Architecture.X64)
        {
            throw new InvalidOperationException(
                "A SQL Server database test reached container startup on an unsupported host. "
                + "Register its database-platform test hook; native x64 CI must execute this case.");
        }
    }

    /// <summary>Recognizes hosted CI without requiring a repository-specific environment variable.</summary>
    private static bool IsContinuousIntegration() =>
        string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase);
}

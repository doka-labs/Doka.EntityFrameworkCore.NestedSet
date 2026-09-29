namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

/// <summary>Runs the actual UserGroups console entry point against isolated SQLite sample databases.</summary>
public sealed class UserSupervisorCommandTests
{
    /// <summary>The real supervisor command prints both group memberships and shadow positions after a move.</summary>
    [Fact]
    public async Task SupervisorScenarioShowsShadowCoordinatesAndRetainedGrantsAsync()
    {
        // Arrange
        await using var directory = new SampleLifecycleDirectory();

        // Act
        var result = await RunAsync(
            directory.DirectoryPath,
            [
                "--provider",
                "sqlite",
                "--scenario",
                "supervisors",
                "--details",
            ]);

        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("After Taylor moves below the Finance director:", result.Output, StringComparison.Ordinal);
        Assert.Contains(
            "Taylor - Software engineer (groups: API team, Finance)",
            result.Output,
            StringComparison.Ordinal);

        Assert.Contains("Morgan - Chief executive (groups: none)", result.Output, StringComparison.Ordinal);
        Assert.Contains("NestedSetPosition=0, Left=5, Right=6", result.Output, StringComparison.Ordinal);
        Assert.Contains("Filtered supervisor: Jordan (Director)", result.Output, StringComparison.Ordinal);
        Assert.Contains(
            "Taylor keeps logs.read and invoices.read from two groups",
            result.Output,
            StringComparison.Ordinal);
    }

    /// <summary>Inspection displays the persisted supervisor tree without changing the SQLite file.</summary>
    [Fact]
    public async Task InspectionReadsSupervisorResultsWithoutMutationAsync()
    {
        // Arrange
        await using var directory = new SampleLifecycleDirectory();
        await SeedAsync(directory.DirectoryPath, ["--provider", "sqlite", "--scenario", "supervisors"]);
        var databasePath = Path.Combine(directory.DirectoryPath, "nestedset_sample_usergroups.sqlite");
        var before = await File.ReadAllBytesAsync(databasePath, CancellationToken.None);

        // Act
        var result = await RunAsync(directory.DirectoryPath, ["--provider", "sqlite", "--inspect", "--details"]);
        var after = await File.ReadAllBytesAsync(databasePath, CancellationToken.None);

        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("Stored supervisor tree: Morgan", result.Output, StringComparison.Ordinal);
        Assert.Contains(
            "Taylor - Software engineer (groups: API team, Finance)",
            result.Output,
            StringComparison.Ordinal);

        Assert.Contains("NestedSetPosition=0, Left=5, Right=6", result.Output, StringComparison.Ordinal);
        Assert.Equal(before, after);
    }

    /// <summary>The default invocation keeps the four previous workflows beside the new fifth one.</summary>
    [Fact]
    public async Task AllScenariosIncludeSupervisorHierarchyAsync()
    {
        // Arrange
        await using var directory = new SampleLifecycleDirectory();

        // Act
        var result = await RunAsync(directory.DirectoryPath, ["--provider", "sqlite"]);

        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("INHERITANCE:", result.Output, StringComparison.Ordinal);
        Assert.Contains("COMMIT:", result.Output, StringComparison.Ordinal);
        Assert.Contains("ROLLBACK:", result.Output, StringComparison.Ordinal);
        Assert.Contains("REJECTED:", result.Output, StringComparison.Ordinal);
        Assert.Contains("SUPERVISORS:", result.Output, StringComparison.Ordinal);
        Assert.Contains("Selected scenarios completed", result.Output, StringComparison.Ordinal);
    }

    /// <summary>Design-time creation accepts every name the console advertises without connecting to a server.</summary>
    [Fact]
    public void DokaDesignTimeFactoryAcceptsSupervisorScenario()
    {
        // Arrange
        var factory = new UserGroupContextFactory();

        // Act
        using var context = factory.CreateDbContext(["--scenario", "supervisors"]);

        // Assert
        Assert.Equal("Doka.EntityFrameworkCore.MySql", context.Database.ProviderName);
    }

    /// <summary>Starts only the already-built sample assembly and returns its captured process result.</summary>
    /// <param name="directory">A test-owned directory for the sample's fixed SQLite filename.</param>
    /// <param name="arguments">The exact console options for one invocation.</param>
    /// <returns>The exit code and separate standard streams.</returns>
    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        string directory,
        IReadOnlyList<string> arguments
    )
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add(typeof(User).Assembly.Location);

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["NESTEDSET_SAMPLE_SQLITE_DIRECTORY"] = directory;

        using var process = new Process();
        process.StartInfo = startInfo;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        // WHY: Process.Start launches synchronously; asynchronous waiting and stream reads avoid blocking the test.
        if (!process.Start())
        {
            throw new InvalidOperationException("The UserGroups sample process did not start.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);

        return (process.ExitCode, await outputTask, await errorTask);
    }

    /// <summary>Creates the prior sample database as test setup and stops on a failed seed command.</summary>
    /// <param name="directory">A test-owned directory for the sample's fixed SQLite filename.</param>
    /// <param name="arguments">The console options that produce the seed database.</param>
    /// <returns>A task that completes when a successful prior run is available for inspection.</returns>
    private static async Task SeedAsync(
        string directory,
        IReadOnlyList<string> arguments
    )
    {
        var result = await RunAsync(directory, arguments);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException("The UserGroups sample setup failed: " + result.Error);
        }
    }
}

using UserGroupsProgram = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups.Program;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

/// <summary>Runs the shared UserGroups entry point against isolated SQLite sample databases.</summary>
public sealed partial class UserSupervisorCommandTests
{
    /// <summary>The real supervisor command prints both group memberships and shadow positions after a move.</summary>
    [Fact]
    public async Task RunAsync_SupervisorScenario_ShowsShadowCoordinatesAndRetainedGrants()
    {
        // Arrange
        await using var directory = new SampleLifecycleDirectory();

        // Act
        var result = await RunAsync(
            directory.DirectoryPath,
            ["--provider", "sqlite", "--scenario", "supervisors", "--details"],
            cancellationToken: TestContext.Current.CancellationToken);

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
    public async Task RunAsync_Inspection_ReadsSupervisorResultsWithoutMutation()
    {
        // Arrange
        await using var directory = new SampleLifecycleDirectory();
        await SeedAsync(
            directory.DirectoryPath,
            ["--provider", "sqlite", "--scenario", "supervisors"],
            cancellationToken: TestContext.Current.CancellationToken);

        var databasePath = Path.Combine(directory.DirectoryPath, "nestedset_sample_usergroups.sqlite");
        var before = await File.ReadAllBytesAsync(databasePath, TestContext.Current.CancellationToken);

        // Act
        var result = await RunAsync(
            directory.DirectoryPath,
            ["--provider", "sqlite", "--inspect", "--details"],
            cancellationToken: TestContext.Current.CancellationToken);

        var after = await File.ReadAllBytesAsync(databasePath, TestContext.Current.CancellationToken);

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
    public async Task RunAsync_AllScenarios_IncludeSupervisorHierarchy()
    {
        // Arrange
        await using var directory = new SampleLifecycleDirectory();

        // Act
        var result = await RunAsync(
            directory.DirectoryPath,
            ["--provider", "sqlite"],
            cancellationToken: TestContext.Current.CancellationToken);

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

    /// <summary>
    /// Design-time creation accepts every name the console advertises without connecting to a server.
    /// </summary>
    [Fact]
    public void CreateDbContext_DokaDesignTimeFactory_AcceptsSupervisorScenario()
    {
        // Arrange
        var factory = new UserGroupContextFactory();

        // Act
        using var context = factory.CreateDbContext(["--scenario", "supervisors"]);

        // Assert
        Assert.Equal("Doka.EntityFrameworkCore.MySql", context.Database.ProviderName);
    }

    /// <summary>Help uses only invocation-owned output and creates no sample database.</summary>
    [Fact]
    public async Task RunAsync_Help_DoesNotCreateDatabase()
    {
        // Arrange
        await using var directory = new SampleLifecycleDirectory();

        // Act
        var result = await RunAsync(directory.DirectoryPath, ["--help"], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("UserGroups sample", result.Output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(directory.DirectoryPath));
    }

    /// <summary>Invalid options fail before endpoint construction and leave the owned directory untouched.</summary>
    [Fact]
    public async Task RunAsync_InvalidScenario_RejectsBeforeDatabaseAccess()
    {
        // Arrange
        await using var directory = new SampleLifecycleDirectory();

        // Act
        var result = await RunAsync(
            directory.DirectoryPath,
            ["--scenario", "unknown-scenario"],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, result.ExitCode);
        Assert.NotEmpty(result.Error);
        Assert.Empty(result.Output);
        Assert.False(Directory.Exists(directory.DirectoryPath));
    }

    /// <summary>Concurrent invocations retain their own databases and output without process-wide settings.</summary>
    [Fact]
    public async Task RunAsync_ConcurrentInvocations_KeepOutputAndStorageIsolated()
    {
        // Arrange
        await using var first = new SampleLifecycleDirectory();
        await using var second = new SampleLifecycleDirectory();

        // Act
        var runs = await Task.WhenAll(
            RunAsync(
                first.DirectoryPath,
                ["--provider", "sqlite", "--scenario", "commit"],
                TestContext.Current.CancellationToken),
            RunAsync(
                second.DirectoryPath,
                ["--provider", "sqlite", "--scenario", "supervisors"],
                TestContext.Current.CancellationToken));

        // Assert
        Assert.All(runs, result => Assert.Equal(0, result.ExitCode));
        Assert.All(runs, result => Assert.Empty(result.Error));
        Assert.Contains(first.DirectoryPath, runs[0].Output, StringComparison.Ordinal);
        Assert.DoesNotContain(second.DirectoryPath, runs[0].Output, StringComparison.Ordinal);
        Assert.Contains("COMMIT:", runs[0].Output, StringComparison.Ordinal);
        Assert.DoesNotContain("SUPERVISORS:", runs[0].Output, StringComparison.Ordinal);
        Assert.Contains(second.DirectoryPath, runs[1].Output, StringComparison.Ordinal);
        Assert.DoesNotContain(first.DirectoryPath, runs[1].Output, StringComparison.Ordinal);
        Assert.Contains("SUPERVISORS:", runs[1].Output, StringComparison.Ordinal);
        Assert.DoesNotContain("COMMIT:", runs[1].Output, StringComparison.Ordinal);
        using var firstFile = File.Open(
            Path.Combine(first.DirectoryPath, "nestedset_sample_usergroups.sqlite"),
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        using var secondFile = File.Open(
            Path.Combine(second.DirectoryPath, "nestedset_sample_usergroups.sqlite"),
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        Assert.True(firstFile.Length > 0);
        Assert.True(secondFile.Length > 0);
    }

    /// <summary>
    /// Cancellation requested by output interrupts forward preparation but still reports its outcome.
    /// </summary>
    [Fact]
    public async Task RunAsync_CancellationDuringInvocation_ReturnsCanceledOutcome()
    {
        // Arrange
        await using var directory = new SampleLifecycleDirectory();
        using var cancellation = new CancellationTokenSource();
        using var output = new CancelOnWriteWriter(cancellation);
        using var error = new StringWriter();

        // Act
        var result = await UserGroupsProgram.RunAsync(
            ["--provider", "sqlite", "--scenario", "commit"],
            output,
            error,
            directory.DirectoryPath,
            cancellation.Token);

        // Assert
        Assert.Equal(130, result);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Contains("Sample canceled", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A failed output sink is reported before any sample database is prepared.</summary>
    [Fact]
    public async Task RunAsync_OutputFailure_ReportsFailureBeforeDatabaseAccess()
    {
        // Arrange
        await using var directory = new SampleLifecycleDirectory();
        var failure = new IOException("The ordinary output sink failed.");
        using var output = new ThrowOnWriteWriter(failure);
        using var error = new StringWriter();

        // Act
        var result = await UserGroupsProgram.RunAsync(
            ["--provider", "sqlite", "--scenario", "commit"],
            output,
            error,
            directory.DirectoryPath,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, result);
        Assert.Contains(failure.Message, error.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(directory.DirectoryPath));
    }

    /// <summary>A failed error sink retains the invalid command as the first failure.</summary>
    [Fact]
    public async Task RunAsync_ErrorReportingFailure_PreservesOriginalFailure()
    {
        // Arrange
        await using var directory = new SampleLifecycleDirectory();
        var failure = new IOException("The error output sink failed.");
        using var output = new StringWriter();
        using var error = new ThrowOnWriteWriter(failure);

        // Act
        var result = await Record.ExceptionAsync(() => UserGroupsProgram.RunAsync(
            ["--scenario", "unknown-scenario"],
            output,
            error,
            directory.DirectoryPath,
            TestContext.Current.CancellationToken));

        // Assert
        var aggregate = Assert.IsType<AggregateException>(result);
        Assert.Equal(2, aggregate.InnerExceptions.Count);
        Assert.IsType<ArgumentException>(aggregate.InnerExceptions[0]);
        Assert.Same(failure, aggregate.InnerExceptions[1]);
        Assert.False(Directory.Exists(directory.DirectoryPath));
    }

    /// <summary>Terminal error-output failure cannot replace the cancellation that stopped forward work.</summary>
    [Fact]
    public async Task RunAsync_CancellationReportingFailure_PreservesCancellationIdentity()
    {
        // Arrange
        await using var directory = new SampleLifecycleDirectory();
        using var cancellation = new CancellationTokenSource();
        var failure = new IOException("The cancellation output sink failed.");
        using var output = new CancelOnWriteWriter(cancellation);
        using var error = new ThrowOnWriteWriter(failure);

        // Act
        var result = await Record.ExceptionAsync(() => UserGroupsProgram.RunAsync(
            ["--provider", "sqlite", "--scenario", "commit"],
            output,
            error,
            directory.DirectoryPath,
            cancellation.Token));

        // Assert
        var aggregate = Assert.IsType<AggregateException>(result);
        Assert.Equal(2, aggregate.InnerExceptions.Count);
        Assert.Equal(cancellation.Token,
            Assert.IsAssignableFrom<OperationCanceledException>(aggregate.InnerExceptions[0]).CancellationToken);
        Assert.Same(failure, aggregate.InnerExceptions[1]);
    }

    /// <summary>The sample entry preserves caller cancellation before creating its database.</summary>
    [Fact]
    public async Task RunAsync_SampleRunner_DoesNotStartForCanceledToken()
    {
        // Arrange
        await using var directory = new SampleLifecycleDirectory();
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();

        // Act
        var error = await Record.ExceptionAsync(() => RunAsync(
            directory.DirectoryPath,
            ["--provider", "sqlite"],
            stop.Token));

        // Assert
        Assert.Equal(
            stop.Token,
            Assert.IsAssignableFrom<OperationCanceledException>(error)
                .CancellationToken);

        Assert.False(Directory.Exists(directory.DirectoryPath));
    }
}

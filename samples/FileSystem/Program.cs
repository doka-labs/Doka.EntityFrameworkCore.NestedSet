namespace Doka.EntityFrameworkCore.NestedSet.Samples.FileSystem;

/// <summary>Runs independently initialized folder scenarios or inspects previously persisted results.</summary>
internal static class Program
{
    /// <summary>Gets the independently selectable demonstrations in their introductory order.</summary>
    internal static IReadOnlyList<string> Scenarios { get; } =
    [
        "queries",
        "rename",
        "move",
        "delete",
        "bulk",
        "maintenance",
        "rejected",
    ];

    /// <summary>Validates the command, prepares only the owned database, and runs the selected flow.</summary>
    /// <param name="args">The sample options shown by --help.</param>
    /// <returns>Zero on success, one on failure, or 130 when Ctrl+C cancels the invocation.</returns>
    public static async Task<int> Main(
        string[] args
    )
    {
        using var cancellation = new SampleCancellation();
        var token = cancellation.Token;

        try
        {
            var command = SampleCommand.Parse(args, Scenarios);

            if (command.Help)
            {
                await PrintHelpAsync(token);

                return 0;
            }

            var configuration = SampleDatabaseConfiguration.Create("filesystem", command.Provider);
            await SampleConsole.WriteLineAsync(configuration.Description, token);

            if (command.Inspect)
            {
                await using var inspection = CreateContext(configuration, readOnly: true);
                await SampleDatabase.EnsureAvailableForInspectionAsync(inspection, token);
                await FolderDisplay.StoredTreesAsync(inspection, command.Details, token);

                return 0;
            }

            await using (var preparation = CreateContext(configuration))
            {
                await SampleDatabase.PrepareAsync(preparation, configuration, command.Reset, token);
            }

            var selected = command.Scenario == "all" ? Scenarios : [command.Scenario];

            foreach (var scenario in selected)
            {
                // WHY: Each scenario owns its tracker and deterministic forest; all does not rely on previous state.
                await using var context = CreateContext(configuration);
                await RunScenarioAsync(context, scenario, command.Details, token);
            }

            await SampleConsole.WriteLineAsync(
                "Selected FileSystem scenarios passed. Results remain stored; use --inspect to view them.",
                token);

            return 0;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            await Console.Error.WriteLineAsync(
                "FileSystem canceled; persisted results remain available.".AsMemory(),
                CancellationToken.None);

            return 130;
        }
        catch (Exception exception) when (exception is ArgumentException
                                              or InvalidOperationException
                                              or DbException
                                              or IOException)
        {
            await Console.Error.WriteLineAsync(
                $"FileSystem failed: {exception.Message}".AsMemory(),
                CancellationToken.None);

            return 1;
        }
    }

    /// <summary>Selects the context type whose checked-in migrations match the chosen provider.</summary>
    private static FileSystemContext CreateContext(
        SampleDatabaseConfiguration configuration,
        bool readOnly = false
    ) => configuration.Provider == SampleProvider.Sqlite
        ? new FileSystemSqliteContext(configuration.CreateOptions<FileSystemSqliteContext>(readOnly))
        : new FileSystemContext(configuration.CreateOptions<FileSystemContext>(readOnly));

    /// <summary>Dispatches one small domain flow without a separate scenario framework.</summary>
    private static Task RunScenarioAsync(
        FileSystemContext context,
        string scenario,
        bool details,
        CancellationToken cancellationToken
    ) => scenario switch
    {
        "queries" => QueryScenario.RunAsync(context, details, cancellationToken),
        "rename" => RenameScenario.RunAsync(context, details, cancellationToken),
        "move" => MoveScenario.RunAsync(context, details, cancellationToken),
        "delete" => DeleteScenario.RunAsync(context, details, cancellationToken),
        "bulk" => BulkScenario.RunAsync(context, details, cancellationToken),
        "maintenance" => MaintenanceScenario.RunAsync(context, details, cancellationToken),
        "rejected" => RejectedOperationScenario.RunAsync(context, details, cancellationToken),
        _ => throw new ArgumentException("The scenario is not implemented.", nameof(scenario)),
    };

    /// <summary>Explains the command contract without resolving an endpoint or touching a database.</summary>
    private static async Task PrintHelpAsync(
        CancellationToken cancellationToken
    )
    {
        await SampleConsole.WriteLineAsync(
            "FileSystem: independent unscoped folder trees with Doka.",
            cancellationToken);

        await SampleConsole.WriteLineAsync(
            "Usage: dotnet run --project samples/FileSystem -- [--provider mariadb|mysql|sqlite]",
            cancellationToken);

        await SampleConsole.WriteLineAsync(
            "       [--scenario NAME|all] [--reset] [--details] | --inspect [--details] | --help",
            cancellationToken);

        await SampleConsole.WriteLineAsync(
            $"Scenarios: {string.Join(", ", Scenarios)}; default: all.",
            cancellationToken);

        await SampleConsole.WriteLineAsync(
            "Provider default: mariadb. --reset recreates only nestedset_sample_filesystem.",
            cancellationToken);

        await SampleConsole.WriteLineAsync(
            "A repeated run requires --reset. --inspect performs reads only.",
            cancellationToken);
    }
}

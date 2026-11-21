namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Runs or inspects independent tenant hierarchy examples against a sample-owned database.</summary>
internal static partial class Program
{
    /// <summary>Gets the independently selectable demonstrations in their introductory order.</summary>
    internal static IReadOnlyList<string> Scenarios { get; } =
    [
        "inheritance",
        "commit",
        "rollback",
        "rejected",
        "supervisors",
        "team-supervisors",
        "group-level-supervisors",
        "ancestor-selection",
        "supervisor-chain",
    ];

    /// <summary>Connects Ctrl+C and the process streams to the independently callable sample entry.</summary>
    /// <param name="args">The options described by --help.</param>
    /// <returns>Zero on success, 130 for cancellation, or one for invalid input or a failed scenario.</returns>
    public static async Task<int> Main(
        string[] args
    )
    {
        using var cancellation = new SampleCancellation();

        try
        {
            return await RunAsync(args, Console.Out, Console.Error, null, cancellation.Token);
        }
        catch (OperationCanceledException failure) when (cancellation.Token.IsCancellationRequested)
        {
            return await ReportCancellationAsync(Console.Error, failure);
        }
    }

    /// <summary>Runs one invocation using caller-owned output and an explicit optional SQLite directory.</summary>
    /// <param name="args">The options described by --help.</param>
    /// <param name="output">The writer receiving ordinary sample output.</param>
    /// <param name="error">The writer receiving failures without changing the process console.</param>
    /// <param name="sqliteDirectory">The owned SQLite directory, or null for the documented console default.</param>
    /// <param name="cancellationToken">Cancels forward work while preserving required resource cleanup.</param>
    /// <returns>Zero on success, 130 for an interrupted invocation, or one for invalid input or failure.</returns>
    internal static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        string? sqliteDirectory,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var command = SampleCommand.Parse(args, Scenarios);

            if (command.Help)
            {
                await PrintHelpAsync(output, cancellationToken);

                return 0;
            }

            var configuration = SampleDatabaseConfiguration.Create(
                "usergroups",
                command.Provider,
                sqliteDirectory: sqliteDirectory);

            await SampleConsole.WriteLineAsync(output, configuration.Description, cancellationToken);
            await ExecuteAsync(command, configuration, output, cancellationToken);

            return 0;
        }
        catch (OperationCanceledException failure) when (cancellationToken.IsCancellationRequested)
        {
            return await ReportCancellationAsync(error, failure);
        }
        catch (Exception exception)
        {
            try
            {
                await error.WriteLineAsync(exception.Message.AsMemory(), CancellationToken.None);
            }
            catch (Exception reportingFailure)
            {
                // WHY: An unavailable caller-owned error sink must retain the original operation failure.
                throw new AggregateException("The sample failed and error reporting also failed.",
                    exception, reportingFailure);
            }

            return 1;
        }
    }

    /// <summary>Completes terminal cancellation reporting independently of the interrupted forward token.</summary>
    private static async Task<int> ReportCancellationAsync(
        TextWriter error,
        OperationCanceledException failure
    )
    {
        // WHY: Both pre-entry Ctrl+C and cancellation during database work retain the console's exit contract.
        try
        {
            await error.WriteLineAsync(
                "Sample canceled; inspect stored results before choosing a reset.".AsMemory(),
                CancellationToken.None);
        }
        catch (Exception reportingFailure)
        {
            throw new AggregateException("The sample was canceled and error reporting also failed.",
                failure, reportingFailure);
        }

        return 130;
    }

    /// <summary>Owns preparation or read-only inspection before dispatching the selected scenarios.</summary>
    private static async Task ExecuteAsync(
        SampleCommand command,
        SampleDatabaseConfiguration configuration,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        if (command.Inspect)
        {
            await using var inspection = UserGroupContexts.Create(configuration, readOnly: true);
            await SampleDatabase.EnsureAvailableForInspectionAsync(inspection, cancellationToken);

            // WHY: Inspection must not silently migrate a database created from a superseded sample migration.
            if ((await inspection.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
            {
                throw new InvalidOperationException(
                    "The stored UserGroups sample uses an older schema. "
                    + "Use --reset only to discard and recreate its data.");
            }

            await GroupOutput.InspectAsync(inspection, command.Details, output, cancellationToken);

            return;
        }

        await using (var setup = UserGroupContexts.Create(configuration))
        {
            await SampleDatabase.PrepareAsync(setup, configuration, command.Reset, cancellationToken);
        }

        var selected = command.Scenario == "all"
            ? Scenarios
            : [command.Scenario];

        // WHY: Scenarios share this invocation's database and run in the documented introductory order.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < selected.Count; index++)
        {
            await RunScenarioAsync(selected[index], configuration, command.Details, output, cancellationToken);
        }

        await SampleConsole.HeadingAsync(
            output,
            "Selected scenarios completed; results remain available for --inspect.",
            cancellationToken);
    }
}

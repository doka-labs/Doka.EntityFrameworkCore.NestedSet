namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Starts one independent SignalLab scenario, all scenarios, or read-only inspection.</summary>
internal static class Program
{
    /// <summary>Gets the implemented scenario names in their documented execution order.</summary>
    internal static IReadOnlyList<string> ScenarioNames { get; } =
    [
        "aggregate",
        "order",
        "update",
        "rejected",
        "report",
        "temporal",
        "metadata",
        "nearest",
    ];

    /// <summary>Runs the selected sample route and retains the database for Rider inspection.</summary>
    /// <param name="args">The documented provider, scenario, inspection, reset and output options.</param>
    /// <returns>Zero on success, 130 on cancellation, or one for an invalid command or failed expectation.</returns>
    public static async Task<int> Main(
        string[] args
    )
    {
        using var cancellation = new SampleCancellation();
        var cancellationToken = cancellation.Token;

        try
        {
            var command = SampleCommand.Parse(args, ScenarioNames);
            if (command.Help)
            {
                await PrintHelpAsync(cancellationToken);

                return 0;
            }

            var configuration = SampleDatabaseConfiguration.Create("signal-lab", command.Provider);
            await SampleConsole.WriteLineAsync(configuration.Description, cancellationToken);

            await using (var context = CreateContext(configuration, command.Inspect))
            {
                if (command.Inspect)
                {
                    await SampleDatabase.EnsureAvailableForInspectionAsync(context, cancellationToken);
                    await InstrumentScenarioSupport.InspectAsync(context, command.Details, cancellationToken);

                    return 0;
                }

                await SampleDatabase.PrepareAsync(context, configuration, command.Reset, cancellationToken);
            }

            var selected = command.Scenario == "all" ? ScenarioNames : new[] { command.Scenario };

            foreach (var scenario in selected)
            {
                // WHY: A fresh EF unit of work prevents tracked state from an earlier scenario affecting this flow.
                await using var context = CreateContext(configuration, readOnly: false);

                switch (scenario)
                {
                    case "report":
                    case "temporal":
                    case "metadata":
                    case "nearest":
                        await ReportingScenario.RunAsync(context, scenario, command.Details, cancellationToken);
                        break;
                    case "aggregate":
                        await AggregateScenario.RunAsync(context, command.Details, cancellationToken);
                        break;
                    case "order":
                        await OrderScenario.RunAsync(context, command.Details, cancellationToken);
                        break;
                    case "update":
                        await UpdateScenario.RunAsync(context, command.Details, cancellationToken);
                        break;
                    case "rejected":
                        await RejectedScenario.RunAsync(context, command.Details, cancellationToken);
                        break;
                }
            }

            await SampleConsole.HeadingAsync(
                "SignalLab scenarios completed. Results remain available for --inspect.",
                cancellationToken);

            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await SampleConsole.WriteLineAsync(
                "Canceled. Retained data can be inspected; use --reset for a fresh run.",
                CancellationToken.None);

            return 130;
        }
        catch (Exception exception) when (exception is ArgumentException
                                              or InvalidOperationException
                                              or DbException
                                              or IOException)
        {
            await SampleConsole.WriteLineAsync($"SignalLab sample failed: {exception.Message}", CancellationToken.None);

            return 1;
        }
    }

    /// <summary>Selects the concrete context associated with the checked-in provider migration set.</summary>
    /// <param name="configuration">The validated sample-owned connection configuration.</param>
    /// <param name="readOnly">Whether inspection should open SQLite without write or create access.</param>
    /// <returns>A caller-owned context that has not connected to the database.</returns>
    private static SignalLabContext CreateContext(
        SampleDatabaseConfiguration configuration,
        bool readOnly
    ) => configuration.Provider == SampleProvider.Sqlite
        ? new SignalLabSqliteContext(configuration.CreateOptions<SignalLabSqliteContext>(readOnly))
        : new SignalLabContext(configuration.CreateOptions<SignalLabContext>(readOnly));

    /// <summary>Prints usage without resolving database configuration or opening a connection.</summary>
    /// <param name="cancellationToken">The token for console output.</param>
    /// <returns>A task that completes after printing the usage text.</returns>
    private static async Task PrintHelpAsync(
        CancellationToken cancellationToken
    )
    {
        await SampleConsole.WriteLineAsync(
            "SignalLab sample: existing-model mapping, SQL aggregation and manual ordering.",
            cancellationToken);

        await SampleConsole.WriteLineAsync("dotnet run --project samples/SignalLab -- [options]", cancellationToken);
        await SampleConsole.WriteLineAsync(
            "  --provider mariadb|mysql|sqlite   Doka MariaDB 11.8 by default",
            cancellationToken);

        await SampleConsole.WriteLineAsync(
            "  --scenario aggregate|order|update|rejected|report|temporal|metadata|nearest|all   all by default",
            cancellationToken);

        await SampleConsole.WriteLineAsync(
            "  --inspect   Read retained results without migration, seeding or writes",
            cancellationToken);

        await SampleConsole.WriteLineAsync(
            "  --reset     Reset only nestedset_sample_signal_lab before the selected flow",
            cancellationToken);

        await SampleConsole.WriteLineAsync(
            "  --details   Include identity, depth, position and bounds",
            cancellationToken);

        await SampleConsole.WriteLineAsync("  --help      Print this help without database access", cancellationToken);
    }
}

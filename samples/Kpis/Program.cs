namespace Doka.EntityFrameworkCore.NestedSet.Samples.Kpis;

/// <summary>Starts one independent KPI scenario, all four scenarios, or read-only inspection.</summary>
internal static class Program
{
    /// <summary>Gets the implemented scenario names in their documented execution order.</summary>
    internal static IReadOnlyList<string> ScenarioNames { get; } = ["aggregate", "order", "update", "rejected"];

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

            var configuration = SampleDatabaseConfiguration.Create("kpis", command.Provider);
            await SampleConsole.WriteLineAsync(configuration.Description, cancellationToken);

            await using (var context = CreateContext(configuration, command.Inspect))
            {
                if (command.Inspect)
                {
                    await SampleDatabase.EnsureAvailableForInspectionAsync(context, cancellationToken);
                    await KpiScenarioSupport.InspectAsync(context, command.Details, cancellationToken);

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
                "KPI scenarios completed. Results remain available for --inspect.",
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
            await SampleConsole.WriteLineAsync($"KPI sample failed: {exception.Message}", CancellationToken.None);

            return 1;
        }
    }

    /// <summary>Selects the concrete context associated with the checked-in provider migration set.</summary>
    /// <param name="configuration">The validated sample-owned connection configuration.</param>
    /// <param name="readOnly">Whether inspection should open SQLite without write or create access.</param>
    /// <returns>A caller-owned context that has not connected to the database.</returns>
    private static KpiContext CreateContext(
        SampleDatabaseConfiguration configuration,
        bool readOnly
    ) => configuration.Provider == SampleProvider.Sqlite
        ? new KpiSqliteContext(configuration.CreateOptions<KpiSqliteContext>(readOnly))
        : new KpiContext(configuration.CreateOptions<KpiContext>(readOnly));

    /// <summary>Prints usage without resolving database configuration or opening a connection.</summary>
    /// <param name="cancellationToken">The token for console output.</param>
    /// <returns>A task that completes after printing the usage text.</returns>
    private static async Task PrintHelpAsync(
        CancellationToken cancellationToken
    )
    {
        await SampleConsole.WriteLineAsync(
            "KPI sample: existing-model mapping, SQL aggregation and manual ordering.",
            cancellationToken);

        await SampleConsole.WriteLineAsync("dotnet run --project samples/Kpis -- [options]", cancellationToken);
        await SampleConsole.WriteLineAsync(
            "  --provider mariadb|mysql|sqlite   Doka MariaDB 11.8 by default",
            cancellationToken);

        await SampleConsole.WriteLineAsync(
            "  --scenario aggregate|order|update|rejected|all   all by default",
            cancellationToken);

        await SampleConsole.WriteLineAsync(
            "  --inspect   Read retained results without migration, seeding or writes",
            cancellationToken);

        await SampleConsole.WriteLineAsync(
            "  --reset     Reset only nestedset_sample_kpis before the selected flow",
            cancellationToken);

        await SampleConsole.WriteLineAsync(
            "  --details   Include identity, depth, position and bounds",
            cancellationToken);

        await SampleConsole.WriteLineAsync("  --help      Print this help without database access", cancellationToken);
    }
}

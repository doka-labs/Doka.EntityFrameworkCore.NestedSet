namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Runs or inspects independent tenant hierarchy examples against a sample-owned database.</summary>
internal static class Program
{
    /// <summary>Gets the independently selectable demonstrations in their introductory order.</summary>
    internal static IReadOnlyList<string> Scenarios { get; } =
    [
        "inheritance",
        "commit",
        "rollback",
        "rejected",
        "supervisors",
    ];

    /// <summary>Validates options before database access and preserves stored results after each run.</summary>
    /// <param name="args">The options described by --help.</param>
    /// <returns>Zero on success, 130 for cancellation, or one for invalid input or a failed scenario.</returns>
    public static async Task<int> Main(
        string[] args
    )
    {
        using var cancellation = new SampleCancellation();

        try
        {
            var command = SampleCommand.Parse(args, Scenarios);

            if (command.Help)
            {
                await PrintHelpAsync(cancellation.Token);

                return 0;
            }

            var configuration = SampleDatabaseConfiguration.Create("usergroups", command.Provider);

            await SampleConsole.WriteLineAsync(configuration.Description, cancellation.Token);

            if (command.Inspect)
            {
                await using var inspection = UserGroupContexts.Create(configuration, readOnly: true);
                await SampleDatabase.EnsureAvailableForInspectionAsync(inspection, cancellation.Token);

                // WHY: Inspection must not silently migrate a database created from a superseded sample migration.
                if ((await inspection.Database.GetPendingMigrationsAsync(cancellation.Token)).Any())
                {
                    throw new InvalidOperationException(
                        "The stored UserGroups sample uses an older schema. Use --reset only to discard and recreate its data.");
                }

                await GroupOutput.InspectAsync(inspection, command.Details, cancellation.Token);

                return 0;
            }

            await using (var setup = UserGroupContexts.Create(configuration))
            {
                await SampleDatabase.PrepareAsync(setup, configuration, command.Reset, cancellation.Token);
            }

            var selected = command.Scenario == "all" ? Scenarios : [command.Scenario];

            foreach (var scenario in selected)
            {
                switch (scenario)
                {
                    case "inheritance":
                        await InheritanceScenario.RunAsync(configuration, command.Details, cancellation.Token);

                        break;

                    case "commit":
                        await CommitScenario.RunAsync(configuration, command.Details, cancellation.Token);

                        break;

                    case "rollback":
                        await RollbackScenario.RunAsync(configuration, command.Details, cancellation.Token);

                        break;

                    case "rejected":
                        await RejectedScenario.RunAsync(configuration, command.Details, cancellation.Token);

                        break;

                    case "supervisors":
                        await SupervisorScenario.RunAsync(configuration, command.Details, cancellation.Token);

                        break;
                }
            }

            await SampleConsole.HeadingAsync(
                "Selected scenarios completed; results remain available for --inspect.",
                cancellation.Token);

            return 0;
        }
        catch (OperationCanceledException) when (cancellation.Token.IsCancellationRequested)
        {
            await Console.Error.WriteLineAsync(
                "Sample canceled; inspect stored results before choosing a reset.".AsMemory(),
                CancellationToken.None);

            return 130;
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync(exception.Message.AsMemory(), CancellationToken.None);

            return 1;
        }
    }

    /// <summary>Prints usage without constructing provider options or opening a connection.</summary>
    private static Task PrintHelpAsync(
        CancellationToken cancellationToken
    ) => SampleConsole.WriteLineAsync(
        "UserGroups sample\n"
        + "  --provider mariadb|mysql|sqlite   Doka MariaDB is the default.\n"
        + "  --scenario inheritance|commit|rollback|rejected|supervisors|all\n"
        + "  --inspect                       Read existing results without changing them.\n"
        + "  --reset                         Reset only nestedset_sample_usergroups, then run.\n"
        + "  --details                       Include tenant, tree and structural coordinates.\n"
        + "  --help                          Show usage without database access.",
        cancellationToken);
}

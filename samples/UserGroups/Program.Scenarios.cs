namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

internal static partial class Program
{
    /// <summary>Dispatches only the scenarios already validated by the command parser.</summary>
    private static Task RunScenarioAsync(
        string scenario,
        SampleDatabaseConfiguration configuration,
        bool details,
        TextWriter output,
        CancellationToken cancellationToken
    ) => scenario switch
    {
        "inheritance" => InheritanceScenario.RunAsync(configuration, details, output, cancellationToken),
        "commit" => CommitScenario.RunAsync(configuration, details, output, cancellationToken),
        "rollback" => RollbackScenario.RunAsync(configuration, details, output, cancellationToken),
        "rejected" => RejectedScenario.RunAsync(configuration, details, output, cancellationToken),
        "supervisors" => SupervisorScenario.RunAsync(configuration, details, output, cancellationToken),
        "team-supervisors" or "group-level-supervisors" or "ancestor-selection" or "supervisor-chain"
            => GroupSupervisorScenario.RunAsync(configuration, scenario, details, output, cancellationToken),
        _ => throw new ArgumentException("Unknown UserGroups scenario.", nameof(scenario)),
    };

    /// <summary>Prints usage without constructing provider options or opening a connection.</summary>
    private static Task PrintHelpAsync(
        TextWriter output,
        CancellationToken cancellationToken
    ) => SampleConsole.WriteLineAsync(
        output,
        "UserGroups sample\n"
        + "  --provider mariadb|mysql|sqlite   Doka MariaDB is the default.\n"
        + "  --scenario inheritance|commit|rollback|rejected|supervisors|team-supervisors|"
        + "group-level-supervisors|ancestor-selection|supervisor-chain|all\n"
        + "  --inspect                       Read existing results without changing them.\n"
        + "  --reset                         Reset only nestedset_sample_usergroups, then run.\n"
        + "  --details                       Include tenant, tree and structural coordinates.\n"
        + "  --help                          Show usage without database access.",
        cancellationToken);
}

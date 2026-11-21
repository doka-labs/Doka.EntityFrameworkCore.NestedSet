namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>
/// Demonstrates explicit team, classification, rank, and chain rules without domain rules in NestedSet.
/// </summary>
internal static partial class GroupSupervisorScenario
{
    /// <summary>Runs one independently selected upward-query flow using fresh tenant-owned sample data.</summary>
    /// <param name="output">The writer owned by this invocation.</param>
    internal static async Task RunAsync(
        SampleDatabaseConfiguration configuration,
        string scenario,
        bool details,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        await using var context = UserGroupContexts.Create(configuration);
        var fixture = await SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), cancellationToken);
        var request = fixture.Request(group: fixture.Groups[5]);
        await SampleConsole.HeadingAsync(output, scenario.ToUpperInvariant(), cancellationToken);
        await SampleConsole.WriteLineAsync(
            output,
            "Group path: G0 -> G1 -> G2 -> G3 -> G4 -> G5; Independent team branches from G1.",
            cancellationToken);

        await SampleConsole.WriteLineAsync(
            output,
            "Group types: G0/G1/G2=Manager; G3=Supervisor; G4=Foreman; G5/Independent team=User team.",
            cancellationToken);

        switch (scenario)
        {
            case "team-supervisors":
                await RunTeamAsync(context, fixture, request, details, output, cancellationToken);

                break;
            case "group-level-supervisors":
                await RunGroupLevelsAsync(context, fixture, request, details, output, cancellationToken);

                break;
            case "ancestor-selection":
                await RunAncestorSelectionAsync(context, fixture, request, details, output, cancellationToken);

                break;
            case "supervisor-chain":
                await RunChainAsync(context, fixture, request, details, output, cancellationToken);

                break;
            default:
                throw new ArgumentException("Unknown upward-query scenario.", nameof(scenario));
        }
    }
}

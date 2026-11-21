namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

internal static partial class GroupSupervisorScenario
{
    /// <summary>Prints person evidence with explicit origins rather than hiding ambiguity behind First.</summary>
    private static async Task PrintAsync(
        string title,
        SupervisorResolutionBatch batch,
        SupervisorScenarioFixture data,
        bool details,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        await SampleConsole.WriteLineAsync(output, $"{title}: {batch.Status}", cancellationToken);
        foreach (var origin in batch.Origins.OrderBy(row => row.StartGroupId))
        {
            var originLabel = origin.StartGroupId is { } group
                ? SupervisorScenarioData.GroupLabel(data, group)
                : "No membership";

            await SampleConsole.WriteLineAsync(
                output,
                $"  Origin {originLabel}"
                + (details
                    ? $" (group {origin.StartGroupId})"
                    : string.Empty)
                + $": {origin.Status}",
                cancellationToken);

            foreach (var row in origin
                .Evidence
                .OrderBy(row => row.Distance)
                .ThenBy(row => row.CandidateUserId))
            {
                await SampleConsole.WriteLineAsync(
                    output,
                    $"    {SupervisorScenarioData.PersonLabel(data, row.CandidateUserId)}"
                    + $"/{SupervisorScenarioData.GroupLabel(data, row.FoundGroupId)}, distance {row.Distance}, "
                    + $"priority {row.LeadershipPriority}"
                    + (details
                        ? $", user {row.CandidateUserId}, group {row.FoundGroupId}, "
                        + $"tenant {row.TenantId}, tree {row.StartTreeId}"
                        : string.Empty),
                    cancellationToken);
            }
        }
    }

    /// <summary>Prints every successful hop and explicit branch ending; incomplete limits are failures.</summary>
    private static async Task PrintChainAsync(
        string title,
        SupervisorChainResult result,
        SupervisorScenarioFixture data,
        bool details,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        await SampleConsole.WriteLineAsync(output, $"{title}: {result.Status}", cancellationToken);
        // WHY: The completed branch snapshot is indexable; output retains its recorded path order.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var branchIndex = 0; branchIndex < result.Branches.Count; branchIndex++)
        {
            var branch = result.Branches[branchIndex];

            await SampleConsole.WriteLineAsync(
                output,
                $"  Origin {SupervisorScenarioData.GroupLabel(data, branch.InitialGroupId)}: "
                + string.Join(
                    " -> ",
                    branch.Hops.Select(hop =>
                        SupervisorScenarioData.PersonLabel(data, hop.Evidence.CandidateUserId)
                        + "/"
                        + SupervisorScenarioData.GroupLabel(data, hop.Evidence.FoundGroupId)))
                + $"; {branch.Status}",
                cancellationToken);

            if (details)
            {
                // WHY: The retained hop snapshot is indexable and remains unchanged during output.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var hopIndex = 0; hopIndex < branch.Hops.Count; hopIndex++)
                {
                    var hop = branch.Hops[hopIndex];

                    await SampleConsole.WriteLineAsync(
                        output,
                        $"    Hop {hop.Number}: user {hop.Evidence.CandidateUserId}, "
                        + $"group {hop.Evidence.FoundGroupId}, source {hop.Evidence.SourceUserId}, "
                        + $"start {hop.Evidence.StartGroupId}, tenant {hop.Evidence.TenantId}, "
                        + $"tree {hop.Evidence.StartTreeId}, original user {hop.InitialUserId}, "
                        + $"original group {hop.InitialGroupId}, original tree {hop.InitialTreeId}",
                        cancellationToken);
                }
            }
        }

        SampleConsole.Require(result.Complete, "The explicit chain budget was exceeded; this is an incomplete result.");
    }
}

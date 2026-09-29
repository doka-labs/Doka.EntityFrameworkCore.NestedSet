namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Demonstrates a precisely handled hierarchy precondition failure without changing persisted data.</summary>
internal static class RejectedScenario
{
    private static readonly Guid s_tenantId = Guid.Parse("2ac9600e-16a4-430e-84d9-000000000005");
    private static readonly Guid s_treeId = Guid.Parse("2ac9600e-16a4-430e-84d9-000000000105");

    /// <summary>Rejects a cyclic move and checks that the complete persisted structure is unchanged.</summary>
    /// <param name="configuration">The sample-owned database configuration.</param>
    /// <param name="details">Whether tree output includes structural columns.</param>
    /// <param name="cancellationToken">The token used for setup, rejection, and persisted-state verification.</param>
    /// <returns>A task that completes after the expected classified rejection has been verified.</returns>
    public static async Task RunAsync(
        SampleDatabaseConfiguration configuration,
        bool details,
        CancellationToken cancellationToken
    )
    {
        await SampleConsole.HeadingAsync(
            "REJECTED: an ancestor cannot become a child of its own descendant",
            cancellationToken);

        GroupHierarchy hierarchy;
        GroupStructure[] before;

        await using (var setup = UserGroupContexts.Create(configuration))
        {
            hierarchy = await GroupScenarioData.CreateHierarchyAsync(setup, s_tenantId, s_treeId, cancellationToken);
            before = await GroupScenarioData.ReadStructureAsync(setup, s_tenantId, s_treeId, cancellationToken);
            await GroupOutput.TreeAsync(
                setup,
                s_tenantId,
                s_treeId,
                "Before rejected move:",
                details,
                cancellationToken);
        }

        var rejected = false;

        await using (var context = UserGroupContexts.Create(configuration))
        {
            try
            {
                await context
                    .NestedSet<UserGroup>()
                    .ForScope(s_tenantId)
                    .MoveToAsync(hierarchy.Engineering.Id, hierarchy.ApiTeam.Id, cancellationToken);
            }
            catch (NestedSetException exception) when (exception.Code == NestedSetErrorCode.CycleDetected)
            {
                rejected = true;
                await SampleConsole.WriteLineAsync($"Expected rejection: {exception.Code}.", cancellationToken);
            }
        }

        SampleConsole.Require(rejected, "The cyclic move did not produce the expected CycleDetected error code.");

        await using var verification = UserGroupContexts.Create(configuration);
        var after = await GroupScenarioData.ReadStructureAsync(verification, s_tenantId, s_treeId, cancellationToken);

        SampleConsole.Require(
            before.SequenceEqual(after),
            "The rejected cyclic move changed the persisted group structure.");

        await GroupOutput.TreeAsync(
            verification,
            s_tenantId,
            s_treeId,
            "After rejected move:",
            details,
            cancellationToken);

        await GroupScenarioData.ValidateAsync(verification, s_tenantId, s_treeId, cancellationToken);
        await SampleConsole.WriteLineAsync(
            "The rejection preserved every parent, boundary, depth and sibling position.",
            cancellationToken);
    }
}

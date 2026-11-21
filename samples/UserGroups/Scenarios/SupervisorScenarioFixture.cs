namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>
/// Provides explicit application fixture keys without loading anything during candidate query construction.
/// </summary>
/// <param name="TenantId">The isolated fixture tenant.</param>
/// <param name="TreeId">The management tree.</param>
/// <param name="ManagerRoleId">The person-role definition.</param>
/// <param name="Groups">G0 through G5, followed by the independent team.</param>
/// <param name="Users">X, Anna, Alex, Ben, Dana, Carla, and Bea.</param>
internal sealed record SupervisorScenarioFixture(
    Guid TenantId,
    Guid TreeId,
    Guid ManagerRoleId,
    int[] Groups,
    int[] Users
)
{
    /// <summary>Creates an explicit nearest-eligible local-role search with user-to-membership routing.</summary>
    internal SupervisorRequest Request(
        SearchStart start = SearchStart.ParentsOnly,
        int? group = null
    ) => new()
    {
        SourceUserId = Users[0],
        StartGroupId = group,
        SearchStart = start,
        Roles = new MembershipRoleFilter(ManagerRoleId),
        Groups = new NthEligibleGroup([], 1),
        Matches = [new MetadataMatch(MetadataOwner.SourceUser, "Route", MetadataOwner.CandidateMembership, "Route")],
        OnDay = new DateOnly(2026, 10, 16),
    };
}

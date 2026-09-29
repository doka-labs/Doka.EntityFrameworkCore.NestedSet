namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Keeps the explicitly named groups created for one independent scenario together.</summary>
/// <param name="TenantId">The scenario's isolated tenant partition.</param>
/// <param name="TreeId">The stable identity of the scenario's tree.</param>
/// <param name="Organization">The root supplying organization-wide grants.</param>
/// <param name="Engineering">The engineering branch.</param>
/// <param name="Platform">The nested platform branch.</param>
/// <param name="ApiTeam">The leaf with its own persistent role assignment.</param>
/// <param name="Finance">The target branch used to demonstrate changed inheritance.</param>
internal sealed record GroupHierarchy(
    Guid TenantId,
    Guid TreeId,
    UserGroup Organization,
    UserGroup Engineering,
    UserGroup Platform,
    UserGroup ApiTeam,
    UserGroup Finance
);

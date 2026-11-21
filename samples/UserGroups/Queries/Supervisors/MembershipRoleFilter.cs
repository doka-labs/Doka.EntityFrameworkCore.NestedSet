namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Requires a role on the exact candidate membership.</summary>
/// <param name="RoleId">The tenant-owned role.</param>
internal sealed record MembershipRoleFilter(Guid RoleId) : SupervisorRoles;

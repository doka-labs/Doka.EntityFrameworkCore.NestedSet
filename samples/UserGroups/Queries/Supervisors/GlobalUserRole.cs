namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Requires the candidate user's tenant-wide role.</summary>
/// <param name="RoleId">The tenant-owned role.</param>
internal sealed record GlobalUserRole(Guid RoleId) : SupervisorRoles;

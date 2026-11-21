namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Accepts the exact local role or the candidate user's global role without duplicating people.</summary>
/// <param name="RoleId">The tenant-owned role.</param>
internal sealed record EitherRole(Guid RoleId) : SupervisorRoles;

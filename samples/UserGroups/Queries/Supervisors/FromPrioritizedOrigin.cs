namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>
/// Uses the first present allowed origin, retaining its missing or ambiguous outcome without fallback.
/// </summary>
/// <param name="GroupIds">The application-defined ordered, duplicate-free allowed origins.</param>
internal sealed record FromPrioritizedOrigin(IReadOnlyList<int> GroupIds) : AcrossOriginsSelection;

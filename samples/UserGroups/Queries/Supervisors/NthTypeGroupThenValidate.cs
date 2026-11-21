namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Ranks groups of one type before candidate or path validation, without a farther fallback.</summary>
/// <param name="TypeId">The required application group type.</param>
/// <param name="Number">The one-based type-group rank.</param>
internal sealed record NthTypeGroupThenValidate(
    int TypeId,
    int Number
) : SupervisorGroups;

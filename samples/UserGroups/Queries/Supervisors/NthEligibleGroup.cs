namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Selects the one-based qualifying group separately for each origin.</summary>
/// <param name="Types">Permitted application type identities; empty allows all.</param>
/// <param name="Number">The requested one-based group rank.</param>
internal sealed record NthEligibleGroup(
    int[] Types,
    int Number
) : SupervisorGroups;

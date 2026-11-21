namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Selects every group satisfying the complete candidate rule; an empty type list allows all types.</summary>
/// <param name="Types">Permitted application type identities.</param>
internal sealed record AllEligibleGroups(params int[] Types) : SupervisorGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Contains one explicit search and person-selection rule applied at a single hop.</summary>
/// <param name="SearchStart">Whether the hop includes its own membership group.</param>
/// <param name="Roles">The person-role source.</param>
/// <param name="Groups">The group/rank selection.</param>
/// <param name="Matches">The exact scalar owner matches.</param>
/// <param name="OnDay">The inclusive validity date, if requested.</param>
/// <param name="Selection">The person-selection rule.</param>
internal sealed record SupervisorStep(
    SearchStart SearchStart,
    SupervisorRoles Roles,
    SupervisorGroups Groups,
    IReadOnlyList<MetadataMatch> Matches,
    DateOnly? OnDay,
    SupervisorSelection Selection
);

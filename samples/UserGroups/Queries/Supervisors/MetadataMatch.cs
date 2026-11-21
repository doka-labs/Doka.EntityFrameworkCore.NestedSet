namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Names a permitted pair of scalar routing fields on explicit owners.</summary>
/// <param name="SourceOwner">The initial or current routing owner.</param>
/// <param name="SourceFieldCode">The source catalog field.</param>
/// <param name="CandidateOwner">The exact candidate owner.</param>
/// <param name="CandidateFieldCode">The candidate catalog field.</param>
internal sealed record MetadataMatch(
    MetadataOwner SourceOwner,
    string SourceFieldCode,
    MetadataOwner CandidateOwner,
    string CandidateFieldCode
);

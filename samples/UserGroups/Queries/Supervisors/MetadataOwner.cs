namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Identifies the exact source or candidate owner of a routing value.</summary>
internal enum MetadataOwner
{
    /// <summary>The explicitly retained routing user's metadata.</summary>
    SourceUser,

    /// <summary>The routing user's exact origin membership metadata.</summary>
    SourceMembership,

    /// <summary>The routing origin group's metadata.</summary>
    SourceGroup,

    /// <summary>The candidate person's metadata.</summary>
    CandidateUser,

    /// <summary>The candidate's exact found membership metadata.</summary>
    CandidateMembership,

    /// <summary>The found group's metadata.</summary>
    CandidateGroup,
}

namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Reports person-selection outcomes without inventing a winner for equal candidates.</summary>
internal enum SupervisorResolutionStatus
{
    /// <summary>The authorized visible source user does not exist.</summary>
    SourceNotFound,

    /// <summary>No selected visible valid own origin exists.</summary>
    NoVisibleMemberships,

    /// <summary>The requested person selection succeeded.</summary>
    Found,

    /// <summary>The evaluated origin has no qualifying selected person.</summary>
    NotFound,

    /// <summary>The explicit person rule cannot establish one winner.</summary>
    Ambiguous,

    /// <summary>The input evidence is incomplete because its safety budget was exceeded.</summary>
    LimitExceeded,
}

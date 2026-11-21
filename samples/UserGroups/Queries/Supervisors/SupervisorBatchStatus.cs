namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Distinguishes an absent visible source from absent own memberships and evaluated paths.</summary>
internal enum SupervisorBatchStatus
{
    /// <summary>The authorized visible source user does not exist.</summary>
    SourceNotFound,

    /// <summary>The source exists but has no selected visible valid own memberships.</summary>
    NoVisibleMemberships,

    /// <summary>The selected visible origins were evaluated.</summary>
    Completed,
}

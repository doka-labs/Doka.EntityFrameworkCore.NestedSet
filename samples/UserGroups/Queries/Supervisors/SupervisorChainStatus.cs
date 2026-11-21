namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Reports the termination of one independent provenance-preserving chain branch.</summary>
internal enum SupervisorChainStatus
{
    /// <summary>The finite requested sequence completed successfully.</summary>
    ReachedRequestedHop,

    /// <summary>The next evaluated step had no qualifying person.</summary>
    NoSupervisor,

    /// <summary>The next step could not select one person under its explicit rule.</summary>
    Ambiguous,

    /// <summary>The next selected person already appeared on this independent path.</summary>
    CycleDetected,

    /// <summary>A mandatory frontier, visited, or result budget stopped complete evaluation.</summary>
    LimitExceeded,
}

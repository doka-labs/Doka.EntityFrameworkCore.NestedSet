namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Identifies a rejected hierarchy operation without parsing its human-readable message.</summary>
public enum NestedSetErrorCode
{
    /// <summary>The operation was rejected without a more specific classification.</summary>
    OperationRejected = 0,

    /// <summary>A required node does not exist in the selected scope.</summary>
    NodeNotFound = 1,

    /// <summary>The requested parent relationship would create a cycle.</summary>
    CycleDetected = 2,

    /// <summary>Persisted coordinates or adjacency violate the hierarchy invariants.</summary>
    InvalidStructure = 3,

    /// <summary>The change tracker or save integration is incompatible with the requested operation.</summary>
    InvalidContext = 4,

    /// <summary>Transaction, retry, isolation, or savepoint requirements are not met.</summary>
    InvalidTransaction = 5,

    /// <summary>An explicit placement conflicts with a strict sibling-ordering rule.</summary>
    ManualPlacementNotAllowed = 6,

    /// <summary>The imported branches contain invalid or repeated entities.</summary>
    InvalidImport = 8,

    /// <summary>The infrastructure lock write did not report the required affected-row count.</summary>
    LockAcquisitionFailed = 9,

    /// <summary>The selected tree has no active registry row in the bound scope.</summary>
    TreeNotFound = 10,

    /// <summary>The requested TreeId is active already or is reserved by a tombstone.</summary>
    TreeIdUnavailable = 11,

    /// <summary>The administrative purge selected an active TreeId instead of a tombstone.</summary>
    TreeIdNotTombstoned = 12,

    /// <summary>An anchor changed its tree identity between resolution and the protected mutation.</summary>
    ConcurrentTreeIdentity = 13,
}

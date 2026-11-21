namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Defines how an acquired registry identity may transition.</summary>
internal enum NestedSetTreeLockMode
{
    /// <summary>The tree must already have an active registry row.</summary>
    Existing = 0,

    /// <summary>The tree must never have existed and is reserved by this operation.</summary>
    New = 1,

    /// <summary>The tree must exist as a tombstone reserved by a completed delete.</summary>
    Tombstoned = 2,
}

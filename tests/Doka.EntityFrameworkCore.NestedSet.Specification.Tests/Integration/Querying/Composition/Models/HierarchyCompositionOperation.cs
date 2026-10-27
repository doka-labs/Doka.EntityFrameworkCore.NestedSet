namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Names independent composition contracts so provider theory cases remain readable in Rider.</summary>
public enum HierarchyCompositionOperation
{
    /// <summary>Projects strict visible ancestors.</summary>
    Ancestors,

    /// <summary>Projects visible ancestors and the selected source itself.</summary>
    AncestorsOrSelf,

    /// <summary>Projects an inclusive subtree.</summary>
    Subtrees,

    /// <summary>Accepts a complete path whose direct edges all match.</summary>
    AllowedPath,

    /// <summary>Accepts a matching visible ancestor.</summary>
    AnyAncestor,

    /// <summary>Accepts the nearest eligible ancestor when its value matches.</summary>
    NearestAncestor,

    /// <summary>Rejects an empty candidate set instead of selecting a valid default key.</summary>
    MissingNearestOwner,

    /// <summary>Rejects a failing nearest owner's value without falling back to a farther owner.</summary>
    NearestOwnerRejectsValue,

    /// <summary>Rejects a path when an intermediate incoming edge fails.</summary>
    BlockedPath,
}

namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Classifies violations reported by detailed hierarchy validation.</summary>
[PublicAPI]
public enum NestedSetValidationCode
{
    /// <summary>A node has a negative sibling position.</summary>
    NegativePosition = 0,

    /// <summary>A node references a parent absent from the selected scope.</summary>
    MissingParent = 1,

    /// <summary>Two siblings share a position, making their intended order ambiguous.</summary>
    DuplicatePosition = 2,

    /// <summary>A node belongs to a cycle or cannot be reached from a root.</summary>
    CycleOrUnreachableNode = 3,

    /// <summary>One or both interval boundaries differ from ordered adjacency.</summary>
    InvalidBounds = 4,

    /// <summary>The stored depth differs from the node's number of ancestors.</summary>
    InvalidDepth = 5,

    /// <summary>The stored position is not the node's expected dense sibling index.</summary>
    InvalidPosition = 6,

    /// <summary>The selected TreeId does not contain exactly one root.</summary>
    InvalidRootCount = 7,
}

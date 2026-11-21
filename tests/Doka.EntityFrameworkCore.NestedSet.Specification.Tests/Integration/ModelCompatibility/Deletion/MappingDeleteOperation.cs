namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Names the three public deletion operations covered by mapping tests.</summary>
public enum MappingDeleteOperation
{
    /// <summary>Deletes one node and promotes its children.</summary>
    Node,

    /// <summary>Deletes one subtree.</summary>
    Subtree,

    /// <summary>Deletes a complete tree.</summary>
    Tree,
}

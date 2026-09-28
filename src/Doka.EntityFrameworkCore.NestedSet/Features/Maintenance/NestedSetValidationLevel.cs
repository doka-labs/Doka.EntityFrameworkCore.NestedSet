namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Controls the amount of structural validation performed for one tree.</summary>
public enum NestedSetValidationLevel
{
    /// <summary>Checks structural invariants without retaining a repair coordinate for every changed node.</summary>
    Quick = 0,

    /// <summary>Checks complete adjacency, sibling order, depth, and interval consistency.</summary>
    Full = 1,
}

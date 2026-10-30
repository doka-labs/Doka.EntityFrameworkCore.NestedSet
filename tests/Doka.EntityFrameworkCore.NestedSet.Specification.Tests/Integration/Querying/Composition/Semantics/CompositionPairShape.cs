namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Names each public pair projection in the selected-endpoint composition matrix.</summary>
public enum CompositionPairShape
{
    /// <summary>Projects an inclusive subtree below each selected anchor.</summary>
    Subtrees,

    /// <summary>Projects strict ancestors above each selected source.</summary>
    Ancestors,

    /// <summary>Projects strict ancestors and the source's own empty self path.</summary>
    AncestorsOrSelf,
}

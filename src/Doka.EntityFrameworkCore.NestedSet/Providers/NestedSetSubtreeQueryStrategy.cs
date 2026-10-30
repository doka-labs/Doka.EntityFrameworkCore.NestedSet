namespace Doka.EntityFrameworkCore.NestedSet.Providers;

/// <summary>Names the shared relational subtree shapes selected by the actual adapter.</summary>
internal enum NestedSetSubtreeQueryStrategy
{
    /// <summary>Uses the provider-independent correlated structural range join.</summary>
    RangeJoin,

    /// <summary>Unions disjoint selected-key self and strict-descendant relations before mapped hydration.</summary>
    SelectedKeyUnion,

    /// <summary>Expands each related alias through a correlated union over selected ancestor keys.</summary>
    RelatedFirstUnion,
}

namespace Doka.EntityFrameworkCore.NestedSet.Providers;

/// <summary>Preserves the provider-qualified nearest-owner selection and decision boundary.</summary>
internal enum NestedSetNearestQueryStrategy
{
    /// <summary>Projects the complete decision in one depth-ordered scalar owner selection.</summary>
    ProjectedDepth,

    /// <summary>Projects the complete decision in one left-ordered scalar owner selection.</summary>
    ProjectedLeft,
}

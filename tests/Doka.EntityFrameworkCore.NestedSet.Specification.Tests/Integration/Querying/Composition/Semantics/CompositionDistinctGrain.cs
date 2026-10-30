namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Names the consumer-selected occurrence grain used by explicit deduplication tests.</summary>
public enum CompositionDistinctGrain
{
    /// <summary>Preserves the original bag without adding Distinct.</summary>
    Bag,

    /// <summary>Counts one result per final target.</summary>
    Target,

    /// <summary>Counts one result per source and final target.</summary>
    SourceTarget,

    /// <summary>Counts one result per complete source, pivot, and final target.</summary>
    Walk,
}

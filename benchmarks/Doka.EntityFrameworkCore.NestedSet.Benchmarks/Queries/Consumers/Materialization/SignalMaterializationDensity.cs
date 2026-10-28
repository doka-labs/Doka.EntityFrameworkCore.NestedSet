namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Describes how many independent scalar groups the prepared reader supplies.</summary>
public enum SignalMaterializationDensity
{
    /// <summary>No selected group has a persisted value.</summary>
    Empty,

    /// <summary>Only the first two selected groups have persisted values.</summary>
    Sparse,

    /// <summary>Every selected group has a persisted value, including a measured zero.</summary>
    Dense,
}

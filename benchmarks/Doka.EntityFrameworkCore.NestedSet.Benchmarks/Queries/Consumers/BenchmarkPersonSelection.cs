namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Names person-selection cases without exposing sample-internal enums in a public signature.</summary>
public enum BenchmarkPersonSelection
{
    /// <summary>Requires exactly one person per source membership.</summary>
    RequireUnique,

    /// <summary>Chooses the unique highest-priority person, retaining ties as ambiguous.</summary>
    HighestBusinessPriority,

    /// <summary>Requests the second distinct business-ranked person, preserving rank ties.</summary>
    SecondPerson,
}

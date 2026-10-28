namespace Doka.EntityFrameworkCore.NestedSet.Providers;

/// <summary>Selects the SQL form of the independent invalid-child relation for row-independent path rules.</summary>
internal enum NestedSetPathFailureStrategy
{
    /// <summary>Keeps the exact per-occurrence visible-edge count.</summary>
    None,

    /// <summary>Uses the qualified scalar-width grouped failure summaries.</summary>
    ScalarWidth,

    /// <summary>Joins a relational width aggregate before grouping local failure coordinates.</summary>
    RelationalWidth,
}

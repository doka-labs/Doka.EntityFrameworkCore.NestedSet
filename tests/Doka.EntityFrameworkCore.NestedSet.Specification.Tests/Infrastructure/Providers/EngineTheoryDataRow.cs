namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Defines a provider-independent member-data variant with explicit, justified engine exclusions.</summary>
public sealed class EngineTheoryDataRow : TheoryDataRow
{
    /// <summary>Captures arguments and the engines unable to execute this specific variant.</summary>
    /// <param name="data">The arguments passed to the theory method.</param>
    /// <param name="excludedEngines">Exact engine names excluded from this variant.</param>
    /// <param name="reason">The concrete reason for those exclusions.</param>
    public EngineTheoryDataRow(
        object?[] data,
        string[] excludedEngines,
        string reason
    ) : base(data)
    {
        ExcludedEngines = excludedEngines;
        Reason = reason;
    }

    /// <summary>Gets the engines explicitly excluded from this variant.</summary>
    public string[] ExcludedEngines { get; }

    /// <summary>Gets the concrete reason for this variant's exclusions.</summary>
    public string Reason { get; }
}

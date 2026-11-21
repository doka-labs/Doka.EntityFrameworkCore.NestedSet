namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Provides an inline variant with explicit, justified engine exclusions.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class EngineInlineDataAttribute : DataAttribute
{
    private readonly object?[] _data;

    /// <summary>Captures provider-independent test arguments.</summary>
    /// <param name="data">The arguments passed to the theory method.</param>
    public EngineInlineDataAttribute(
        params object?[] data
    )
    {
        _data = data;
    }

    /// <summary>Gets or sets engines that cannot execute this particular variant.</summary>
    public string[] ExcludedEngines { get; set; } = [];

    /// <summary>Gets or sets the concrete reason for excluding engines from this variant.</summary>
    public string? Reason { get; set; }

    /// <inheritdoc />
    public override ValueTask<IReadOnlyCollection<ITheoryDataRow>> GetData(
        MethodInfo testMethod,
        DisposalTracker disposalTracker
    )
    {
        var engine = EngineTestSelection.ResolveEngine(testMethod.ReflectedType!);
        IReadOnlyCollection<ITheoryDataRow> rows = EngineTestSelection.AppliesToEngine(engine, ExcludedEngines, Reason)
            ? [ConvertDataRow(_data)]
            : [];

        return ValueTask.FromResult(rows);
    }

    /// <inheritdoc />
    public override bool SupportsDiscoveryEnumeration() => true;
}

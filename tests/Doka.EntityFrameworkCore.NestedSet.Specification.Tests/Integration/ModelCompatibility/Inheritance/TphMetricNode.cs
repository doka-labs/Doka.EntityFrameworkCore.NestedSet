namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Represents a metric in a TPH hierarchy.</summary>
internal sealed class TphMetricNode : InheritanceNode
{
    /// <summary>Gets or sets metric-specific payload.</summary>
    internal decimal Target { get; set; }
}

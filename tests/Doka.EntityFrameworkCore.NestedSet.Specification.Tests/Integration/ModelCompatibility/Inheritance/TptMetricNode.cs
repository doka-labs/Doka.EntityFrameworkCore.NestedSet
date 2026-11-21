namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Represents a metric stored in another TPT derived table.</summary>
internal sealed class TptMetricNode : InheritanceNode
{
    /// <summary>Gets or sets metric-specific payload.</summary>
    internal string MetricKind { get; set; } = "";
}

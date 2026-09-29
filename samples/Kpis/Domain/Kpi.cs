namespace Doka.EntityFrameworkCore.NestedSet.Samples.Kpis;

/// <summary>Represents an existing metric model adapted without a NestedSet interface or entity base class.</summary>
public sealed class Kpi
{
    /// <summary>Gets the application-assigned metric identity.</summary>
    /// <remarks>The examples supply fixed identities so a reset also reproduces the detailed output.</remarks>
    public Guid NodeId { get; init; } = Guid.NewGuid();

    /// <summary>Gets or sets the displayed service or aggregate name.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets a leaf measurement in successful deployments for the same reporting period.</summary>
    /// <remarks>Aggregate branches have no stored measurement; the application sums their service leaves.</remarks>
    public int? SuccessfulDeployments { get; set; }

    /// <summary>Gets the project partition maintained by the bound hierarchy facade.</summary>
    public Guid ProjectId { get; private set; }

    /// <summary>Gets the stable tree identity inside the project partition.</summary>
    public Guid TreeId { get; private set; }

    /// <summary>Gets the direct aggregate identity, or null for a root.</summary>
    public Guid? ParentMetricId { get; private set; }

    /// <summary>Gets the inclusive left boundary maintained by NestedSet.</summary>
    public long Start { get; private set; }

    /// <summary>Gets the inclusive right boundary maintained by NestedSet.</summary>
    public long End { get; private set; }

    /// <summary>Gets the maintained depth, with zero for a root.</summary>
    public int Level { get; private set; }

    /// <summary>Gets the maintained zero-based position within the direct sibling group.</summary>
    public long SiblingPosition { get; private set; }
}

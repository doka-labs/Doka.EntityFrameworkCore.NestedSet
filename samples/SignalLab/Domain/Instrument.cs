namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Represents a laboratory instrument without a NestedSet interface or entity base class.</summary>
public sealed class Instrument
{
    /// <summary>Gets the application-assigned instrument identity.</summary>
    /// <remarks>The examples supply fixed identities so a reset also reproduces the detailed output.</remarks>
    public Guid NodeId { get; init; } = Guid.NewGuid();

    /// <summary>Gets or sets the instrument or container label.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets a synthetic pulse counter on a sensor leaf.</summary>
    /// <remarks>Containers have no stored count; this scenario sums only its sensor leaves.</remarks>
    public int? PulseCount { get; set; }

    /// <summary>Gets the application-owned laboratory partition maintained by the bound facade.</summary>
    public Guid ProjectId { get; private set; }

    /// <summary>Gets the stable tree identity inside the project partition.</summary>
    public Guid TreeId { get; private set; }

    /// <summary>Gets the direct parent instrument, or null for a station root.</summary>
    public Guid? ParentInstrumentId { get; private set; }

    /// <summary>Gets the inclusive left boundary maintained by NestedSet.</summary>
    public long Start { get; private set; }

    /// <summary>Gets the inclusive right boundary maintained by NestedSet.</summary>
    public long End { get; private set; }

    /// <summary>Gets the maintained depth, with zero for a root.</summary>
    public int Level { get; private set; }

    /// <summary>Gets the maintained zero-based position within the direct sibling group.</summary>
    public long SiblingPosition { get; private set; }
}

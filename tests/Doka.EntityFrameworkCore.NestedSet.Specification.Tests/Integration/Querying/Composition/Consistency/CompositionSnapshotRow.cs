namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Captures structural and independent payload values from one actual SQL result.</summary>
internal sealed record CompositionSnapshotRow
{
    /// <summary>Gets the selected ancestor identity.</summary>
    public int Upper { get; init; }

    /// <summary>Gets the selected lower node's stored direct parent.</summary>
    public int? Parent { get; init; }

    /// <summary>Gets the structural distance from the downward anchor.</summary>
    public int DownDistance { get; init; }

    /// <summary>Gets the structural distance to the upward ancestor.</summary>
    public int UpDistance { get; init; }

    /// <summary>Gets a payload flag changed together with the hierarchy.</summary>
    public bool Matches { get; init; }

    /// <summary>Gets the inclusive incoming-edge start in the same snapshot.</summary>
    public DateOnly FromDate { get; init; }

    /// <summary>Gets the optional inclusive incoming-edge end in the same snapshot.</summary>
    public DateOnly? ToDate { get; init; }

    /// <summary>Gets the value from an independently joined fact table.</summary>
    public int Amount { get; init; }
}

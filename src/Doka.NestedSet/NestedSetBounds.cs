namespace Doka.NestedSet;

/// <summary>Represents positive, ordered nested-set boundaries with an even inclusive width.</summary>
/// <remarks>The default value is invalid and throws when used in calculations or containment checks.</remarks>
public readonly record struct NestedSetBounds
{
    /// <summary>Creates validated nested-set bounds.</summary>
    /// <param name="left">The positive left boundary.</param>
    /// <param name="right">The greater right boundary.</param>
    /// <exception cref="ArgumentOutOfRangeException">The bounds are not positive and ordered.</exception>
    /// <exception cref="ArgumentException">The inclusive width is odd.</exception>
    public NestedSetBounds(
        long left,
        long right
    )
    {
        _ = GetValidatedWidth(left, right);

        Left = left;
        Right = right;
    }

    /// <summary>Gets the inclusive left boundary.</summary>
    /// <value>The positive left boundary, or zero for an invalid default instance.</value>
    public long Left { get; }

    /// <summary>Gets the inclusive right boundary.</summary>
    /// <value>The boundary greater than <see cref="Left" />, or zero for an invalid default instance.</value>
    public long Right { get; }

    /// <summary>Gets the inclusive width after validating the bounds.</summary>
    /// <value>The number of boundary positions occupied by this node and its descendants.</value>
    /// <exception cref="ArgumentOutOfRangeException">This is an invalid default instance.</exception>
    public long Width => GetValidatedWidth(Left, Right);

    /// <summary>Gets whether these bounds describe a leaf.</summary>
    /// <value>True when the valid interval occupies exactly two boundary positions.</value>
    /// <exception cref="ArgumentOutOfRangeException">This is an invalid default instance.</exception>
    public bool IsLeaf => Width == 2;

    /// <summary>Gets the number of descendants in a dense nested-set representation.</summary>
    /// <value>The number of nodes strictly enclosed by the valid interval.</value>
    /// <exception cref="ArgumentOutOfRangeException">This is an invalid default instance.</exception>
    public long DescendantCount => (Width / 2) - 1;

    /// <summary>Formats stored coordinates without evaluating calculations that reject an invalid default.</summary>
    /// <returns>An invariant diagnostic representation containing the left and right boundaries.</returns>
    public override string ToString() =>
        // WHY: Generated record formatting reads every public getter; Width deliberately rejects the default value.
        FormattableString.Invariant($"NestedSetBounds {{ Left = {Left}, Right = {Right} }}");

    /// <summary>Tests strict containment of another node's bounds.</summary>
    /// <param name="other">Bounds from the same tree.</param>
    /// <returns>Whether both boundaries are strictly inside this node.</returns>
    /// <remarks>The caller must ensure both bounds belong to the same tree.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">Either bounds value is an invalid default instance.</exception>
    public bool Contains(
        NestedSetBounds other
    )
    {
        _ = GetValidatedWidth(Left, Right);
        _ = GetValidatedWidth(other.Left, other.Right);

        return Left < other.Left && Right > other.Right;
    }

    /// <summary>Checks the local interval invariants and returns the inclusive width.</summary>
    /// <param name="left">The inclusive left boundary.</param>
    /// <param name="right">The inclusive right boundary.</param>
    /// <returns>The validated inclusive width.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The boundaries are not positive and ordered.</exception>
    /// <exception cref="ArgumentException">The inclusive width is odd.</exception>
    private static long GetValidatedWidth(
        long left,
        long right
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(left);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(right, left);

        // WHY: Checked arithmetic prevents future invariant changes from silently wrapping structural counts.
        var width = checked(right - left + 1);

        // WHY: Every node contributes an entering and an exiting boundary, so a complete interval has even width.
        return (width % 2) == 0
            ? width
            : throw new ArgumentException("Nested-set bounds must have an even inclusive width.", nameof(right));
    }
}

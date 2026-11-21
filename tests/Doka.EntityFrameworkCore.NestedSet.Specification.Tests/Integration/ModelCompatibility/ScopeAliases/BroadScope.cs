namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Uses broad domain equality while retaining its exact stored scope representation.</summary>
internal sealed class BroadScope : IEquatable<BroadScope>
{
    /// <summary>Retains the supplied fixture values without changing their identity.</summary>
    /// <param name="value">The model value retained by this fixture identity.</param>
    public BroadScope(
        string value
    )
    {
        Value = value;
    }

    /// <summary>Gets the scope value stored by the database.</summary>
    internal string Value { get; }

    /// <inheritdoc />
    public bool Equals(
        BroadScope? other
    ) => other is not null && StringComparer.OrdinalIgnoreCase.Equals(Value, other.Value);

    /// <inheritdoc />
    public override bool Equals(
        object? obj
    ) => obj is BroadScope other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);
}

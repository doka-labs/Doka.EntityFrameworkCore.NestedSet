namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Has broader domain equality than its case-sensitive stored tree identity.</summary>
internal sealed class BroadTreeId : IEquatable<BroadTreeId>
{
    /// <summary>Retains the supplied fixture values without changing their identity.</summary>
    /// <param name="value">The model value retained by this fixture identity.</param>
    public BroadTreeId(
        string value
    )
    {
        Value = value;
    }

    /// <summary>Gets the identity written by the value converter.</summary>
    internal string Value { get; }

    /// <inheritdoc />
    public bool Equals(
        BroadTreeId? other
    ) => other is not null && StringComparer.OrdinalIgnoreCase.Equals(Value, other.Value);

    /// <inheritdoc />
    public override bool Equals(
        object? obj
    ) => obj is BroadTreeId other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);
}

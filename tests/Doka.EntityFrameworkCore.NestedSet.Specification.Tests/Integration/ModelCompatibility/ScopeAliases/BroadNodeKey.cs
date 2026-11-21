namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Has broader domain equality than its case-sensitive stored node identity.</summary>
internal sealed class BroadNodeKey : IEquatable<BroadNodeKey>
{
    /// <summary>Retains the supplied fixture values without changing their identity.</summary>
    /// <param name="value">The model value retained by this fixture identity.</param>
    public BroadNodeKey(
        string value
    )
    {
        Value = value;
    }

    /// <summary>Gets the identity written by the value converter.</summary>
    internal string Value { get; }

    /// <inheritdoc />
    public bool Equals(
        BroadNodeKey? other
    ) => other is not null && StringComparer.OrdinalIgnoreCase.Equals(Value, other.Value);

    /// <inheritdoc />
    public override bool Equals(
        object? obj
    ) => obj is BroadNodeKey other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);
}

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Has broader domain equality than its case-sensitive stored tree identity.</summary>
internal sealed class NativeGuardTreeId : IEquatable<NativeGuardTreeId>
{
    /// <summary>Retains the supplied fixture values without changing their identity.</summary>
    /// <param name="value">The model value retained by this fixture identity.</param>
    public NativeGuardTreeId(
        string value
    )
    {
        Value = value;
    }

    /// <summary>Gets the identity written by the value converter.</summary>
    internal string Value { get; }

    /// <inheritdoc />
    public bool Equals(
        NativeGuardTreeId? other
    ) => other is not null && StringComparer.OrdinalIgnoreCase.Equals(Value, other.Value);

    /// <inheritdoc />
    public override bool Equals(
        object? obj
    ) => obj is NativeGuardTreeId other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);
}

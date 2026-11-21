namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Provides an integer-backed identity without CLR equality or ordering operators.</summary>
internal readonly struct CompositionNodeKey : IEquatable<CompositionNodeKey>
{
    /// <summary>Creates a key with an explicitly converted provider representation.</summary>
    internal CompositionNodeKey(
        int value
    )
    {
        Value = value;
    }

    /// <summary>Gets the provider integer represented by this value object.</summary>
    internal int Value { get; }

    /// <inheritdoc />
    public bool Equals(
        CompositionNodeKey other
    ) => Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(
        object? obj
    ) => obj is CompositionNodeKey other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Value;
}

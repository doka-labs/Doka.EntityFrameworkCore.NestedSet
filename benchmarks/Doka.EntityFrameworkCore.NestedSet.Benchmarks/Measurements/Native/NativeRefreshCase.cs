namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Defines a persisted sibling population and its independently tracked refresh subset.</summary>
/// <param name="Children">The persisted children, excluding their root.</param>
/// <param name="Sparse">Whether only the moved child is tracked.</param>
/// <param name="Adjacent">Whether the changed name moves exactly one sibling position.</param>
public sealed record NativeRefreshCase(int Children, bool Sparse, bool Adjacent)
{
    /// <inheritdoc />
    public override string ToString() => $"children-{Children}-sparse-{Sparse}-adjacent-{Adjacent}";
}

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Retains the original grouped source identity, contribution multiplicity and payload checksum.</summary>
internal sealed record NativeCompositionTotal(int Anchor, long Contributions, long Sum);

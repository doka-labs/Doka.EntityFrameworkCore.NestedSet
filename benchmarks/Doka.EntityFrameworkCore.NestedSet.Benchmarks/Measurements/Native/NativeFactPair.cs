namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Retains independent occurrence identity, hierarchy source, selected ancestor and distance.</summary>
internal sealed record NativeFactPair(int Occurrence, int Source, int Owner, int Distance);

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Retains one complete candidate, request and mutation shape without a benchmark-only size limit.</summary>
/// <param name="Keys">The real EF key storage model.</param>
/// <param name="Candidates">The real unrelated tracked leaf population.</param>
/// <param name="Requests">The number of independent existing trees protected by the operation.</param>
/// <param name="Delete">
/// Whether to delete the sole requested root rather than execute a no-op payload.
/// </param>
public sealed record NativeGuardCase(NativeGuardKeyKind Keys, int Candidates, int Requests, bool Delete)
{
    /// <inheritdoc />
    public override string ToString() =>
        $"{Keys}-{Candidates}-candidates-{Requests}-trees-{(Delete ? "delete" : "guard")}";
}

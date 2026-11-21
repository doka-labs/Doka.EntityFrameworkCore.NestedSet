namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Identifies an actual view-adapted capacity consumer construction request.</summary>
/// <param name="Profile">The signal, group, chain-initial, or ordinal construction profile.</param>
/// <param name="Nodes">The original persisted structural population encoded in request keys.</param>
/// <param name="Origins">The original anchor or membership-origin cardinality.</param>
/// <param name="Fields">The original number of combined metadata criteria.</param>
/// <param name="Nearest">Whether a signal request selects its nearest matching owner.</param>
public sealed record CapacityConsumerCase(string Profile, int Nodes, int Origins, int Fields, bool Nearest)
{
    /// <inheritdoc />
    public override string ToString() => $"{Profile}-nodes-{Nodes}-origins-{Origins}-fields-{Fields}-nearest-{Nearest}";
}

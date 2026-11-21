namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>
/// Retains every dense native construction branch without allocating or seeding its physical population.
/// </summary>
/// <param name="Name">The original native failure-placement profile.</param>
/// <param name="Nodes">The bound physical population of each tree.</param>
/// <param name="FirstAnchor">The original chain's first selected upper endpoint.</param>
/// <param name="Branches">Whether the query composes disjoint branch endpoints.</param>
/// <param name="Forest">Whether the public operators retain both authorized tree identities.</param>
public sealed record DenseConstructionCase(string Name, int Nodes, int FirstAnchor, bool Branches, bool Forest)
{
    /// <summary>Gets the final eligible dense ancestor bound.</summary>
    internal int? CandidateCutoff => Name switch
    {
        "DenseCandidatesAccept" or "DenseCandidatesReject" => Nodes - 10,
        "DenseCandidatesPriorAccept" or "DenseCandidatesPriorReject" => Nodes - 1_000,
        _ => null,
    };

    /// <inheritdoc />
    public override string ToString() => $"{Name}-nodes-{Nodes}-branches-{Branches}-forest-{Forest}";
}

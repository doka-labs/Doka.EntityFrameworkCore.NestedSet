namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Defines requested paths independently of the stored nested-set coordinates and SQL buckets.</summary>
/// <param name="Name">The named independent failure-placement control.</param>
/// <param name="Nodes">Nodes in each physical tree, including its root.</param>
/// <param name="FirstAnchor">The first of ten requested upper endpoints in a chain.</param>
/// <param name="DenseFailures">Whether the first half's even children are blocked.</param>
/// <param name="ExtraFailure">One additional blocked child, including a possible upper incoming edge.</param>
/// <param name="Branches">Whether the tree contains one hundred disjoint child chains.</param>
/// <param name="Forest">Whether the public hierarchy selects both trees in scope one.</param>
internal sealed record DensePathWorkScenario(
    string Name,
    int Nodes,
    int FirstAnchor,
    bool DenseFailures,
    int? ExtraFailure,
    bool Branches = false,
    bool Forest = false
)
{
    /// <summary>Defines the independently seeded number of nodes in a complete disjoint branch.</summary>
    internal const int BranchSize = 1_000;

    /// <summary>Uses printable binary bytes so native MariaDB plans need only observed NUL normalization.</summary>
    internal static readonly Guid OtherTree = Guid.Parse("61616161-6161-6161-6161-616161616161");

    /// <summary>Gets the authorized population; foreign scope three never contributes to this budget.</summary>
    internal int VisibleNodes => Forest ? 2 * Nodes : Nodes;

    /// <summary>Gets all physically stored nodes, including both coordinate-overlapping decoy populations.</summary>
    internal int StoredNodes => Branches || Forest ? 3 * Nodes : Nodes;

    /// <summary>Gets the last eligible chain candidate, or null when no dense candidate search is requested.</summary>
    internal int? CandidateCutoff =>
        Name switch
        {
            "DenseCandidatesAccept" or "DenseCandidatesReject" => Nodes - 10,
            "DenseCandidatesPriorAccept" or "DenseCandidatesPriorReject" => Nodes - 1_000,
            _ => null,
        };

    /// <summary>Retains the original three chain placements and the independently rejecting fringe controls.</summary>
    internal static DensePathWorkScenario Chain(
        string shape,
        int nodes
    ) => shape switch
    {
        "DenseOffPath" => new(shape, nodes, nodes - 999, true, null),
        "DenseOnPath" => new(shape, nodes, 1, true, null),
        "CleanLongPath" => new(shape, nodes, 1, false, null),
        "DenseCandidatesAccept" => new(shape, nodes, 1, false, null),
        "DenseCandidatesReject" => new(shape, nodes, 1, false, null),
        "DenseCandidatesPriorAccept" => new(shape, nodes, 1, false, null),
        "DenseCandidatesPriorReject" => new(shape, nodes, 1, false, null),
        "SingleUpperFringe" => new(shape, nodes, 99_020, true, 99_030),
        "SingleLowerFringe" => new(shape, nodes, 99_020, true, 99_995),
        "SameBlockLower" => new(shape, nodes, 99_980, true, 99_995),
        "UpperIncomingOnly" => new(shape, nodes, 99_020, true, 99_020),
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    /// <summary>Creates disjoint paths whose lower endpoints span ten different branches and many key blocks.</summary>
    internal static DensePathWorkScenario Disjoint(
        string shape,
        bool forest
    ) => shape switch
    {
        "DenseOffBranch" => new(
            shape,
            100_000,
            0,
            true,
            null,
            true,
            forest),
        "SelectedBranchFailure" => new(
            shape,
            100_000,
            0,
            true,
            85_752,
            true,
            forest),
        "CleanDistinctLower" => new(
            shape,
            100_000,
            0,
            false,
            null,
            true,
            forest),
        "RootAboveSiblingFailures" => new(
            shape,
            100_000,
            0,
            true,
            null,
            true,
            forest),
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    /// <summary>Enumerates the bounded requested occurrence bag, retaining both authorized forest identities.</summary>
    internal IEnumerable<(int Upper, int Lower)> Pairs()
    {
        if (!Branches)
        {
            for (var population = 0; population < (Forest ? 2 : 1); population++)
            {
                var offset = population * Nodes;

                foreach (var upper in Enumerable.Range(FirstAnchor, 10))
                {
                    foreach (var lower in Enumerable.Range(Nodes - 9, 10))
                    {
                        yield return (offset + upper, offset + lower);
                    }
                }
            }

            yield break;
        }

        for (var population = 0; population < (Forest ? 2 : 1); population++)
        {
            foreach (var branch in Enumerable.Range(80, 10))
            {
                var first = (population * Nodes) + 2 + branch * BranchSize;

                foreach (var offset in Enumerable.Range(990, 10))
                {
                    var upper = Name == "RootAboveSiblingFailures" ? (population * Nodes) + 1 : first + 500;

                    yield return (upper, first + offset);
                }
            }
        }
    }

    /// <summary>Walks Parent links and excludes the upper endpoint's incoming edge without inspecting bounds.</summary>
    internal (CompositionCapacityTotal[] Rows, long RequiredEdges) Expected()
    {
        var totals = new Dictionary<int, (long Count, long Sum)>();
        var required = 0L;

        foreach (var pair in Pairs())
        {
            var current = pair.Lower;
            var edges = 0;
            int? nearestFailureDistanceFromLower = null;
            int? nearestCandidateDistanceFromLower = null;

            while (current != pair.Upper)
            {
                if (Blocked(current))
                {
                    nearestFailureDistanceFromLower = edges;
                }

                if (CandidateCutoff is { } cutoff
                    && (current - 1) % Nodes + 1 <= cutoff
                    && nearestCandidateDistanceFromLower is null)
                {
                    nearestCandidateDistanceFromLower = edges;
                }

                current = Parent(current)
                    ?? throw new InvalidOperationException("The fixture path has no requested upper endpoint.");

                edges++;
            }

            required += nearestCandidateDistanceFromLower ?? edges - (nearestFailureDistanceFromLower ?? 0);

            if (nearestFailureDistanceFromLower is not null)
            {
                continue;
            }

            totals.TryGetValue(pair.Upper, out var total);
            totals[pair.Upper] = (total.Count + 1, total.Sum + pair.Lower);
        }

        // WHY: A farther root matching the value predicate cannot replace the nearest structural candidate.
        if (Name is "DenseCandidatesReject" or "DenseCandidatesPriorReject")
        {
            totals.Clear();
        }

        var rows = totals
            .OrderBy(row => row.Key)
            .Select(row => new CompositionCapacityTotal(row.Key, row.Value.Count, row.Value.Sum))
            .ToArray();

        return (rows, required);
    }

    /// <summary>Derives adjacency solely from the independent native fixture construction plan.</summary>
    private int? Parent(
        int key
    )
    {
        var population = Branches || Forest ? (key - 1) / Nodes : 0;
        var local = key - (population * Nodes);

        if (local == 1)
        {
            return null;
        }

        return Branches && (local - 2) % BranchSize == 0 ? (population * Nodes) + 1 : key - 1;
    }

    /// <summary>Models blocked child edges independently of SQL predicates and coordinate bucketing.</summary>
    private bool Blocked(
        int key
    )
    {
        if (Branches && key > Nodes)
        {
            var local = (key - 1) % Nodes + 1;

            if (Name == "RootAboveSiblingFailures")
            {
                return local != 1;
            }

            // WHY: Upper markers remain eligible nearest owners, but every incoming child below them is blocked.
            return local < 80_502 || local > 89_502 || (local - 80_502) % BranchSize != 0;
        }

        return key == ExtraFailure || (DenseFailures && key <= Nodes / 2 && key % 2 == 0);
    }
}

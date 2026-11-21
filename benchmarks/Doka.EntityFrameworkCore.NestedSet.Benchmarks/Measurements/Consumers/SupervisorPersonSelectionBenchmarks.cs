using Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures complete winner evidence reuse and canonical distinct-person ranking.</summary>
public class SupervisorPersonSelectionBenchmarks
{
    private SupervisorCandidate[]? _candidates;
    private SupervisorSelection? _selection;
    private SupervisorResolution? _result;

    /// <summary>Gets or sets the original complete-owned-bag or high-fan-in rank workload.</summary>
    [Params("OwnedUnique", "OwnedPriority", "Rank")]
    public string Shape { get; set; } = "OwnedUnique";

    /// <summary>Prepares the original 131072-occurrence workload without measured transport allocation.</summary>
    [GlobalSetup]
    public void Initialize()
    {
        _candidates = Shape switch
        {
            "OwnedUnique" or "OwnedPriority" => Enumerable.Range(0, 131_072)
                .Select(index => Candidate(21, index == 0 ? 1 : 2, index == 0 ? 10 : 1))
                .ToArray(),
            "Rank" => CreateRankedEvidence(),
            _ => throw new ArgumentOutOfRangeException(nameof(Shape)),
        };

        _selection = Shape switch
        {
            "OwnedUnique" => SupervisorSelection.RequireUnique,
            "OwnedPriority" => SupervisorSelection.HighestBusinessPriority,
            _ => SupervisorSelection.NthPerson(513),
        };

        _ = Select();
        VerifyWorkload();
    }

    /// <summary>Selects the actual outcome with every winning occurrence and its canonical cursor.</summary>
    /// <returns>The actual complete selected evidence bag.</returns>
    [Benchmark]
    public object Select()
    {
        var candidates = _candidates ?? throw new InvalidOperationException("Prepare person occurrences first.");
        var selection = _selection ?? throw new InvalidOperationException("Prepare person selection first.");
        _result = Shape == "Rank"
            ? SupervisorSelectionRules.Select(7, candidates, selection)
            : SupervisorSelectionRules.SelectOwned(7, candidates, selection);

        return _result;
    }

    /// <summary>Checks independent winner, cardinality and reference-identity oracles outside measurement.</summary>
    [GlobalCleanup]
    public void VerifyWorkload()
    {
        var result = _result ?? throw new InvalidOperationException("Select the prepared person evidence first.");
        var candidates = _candidates!;
        var ranked = Shape == "Rank";
        var expectedContinuation = ranked
            ? candidates[(512 - 1) * 128 + 1]
            : candidates[Shape == "OwnedPriority" ? 1 : 0];
        BenchmarkFixture.Require(
            result.Status == SupervisorResolutionStatus.Found
                && result.Evidence.Count == (ranked ? 128 : 131_072)
                && result.Continuation?.CandidateUserId == (ranked ? 512 : 21)
                && ReferenceEquals(result.Continuation, expectedContinuation),
            "Person selection changed complete evidence or its independent canonical cursor.");
    }

    /// <summary>Creates reversed identities with one canonical slot and 128 occurrences per person.</summary>
    private static SupervisorCandidate[] CreateRankedEvidence()
    {
        const int people = 1024;
        const int occurrences = 128;
        var result = new SupervisorCandidate[people * occurrences];

        // WHY: Independently known rank positions and offsets keep the oracle separate from selection.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var rank = 0; rank < people; rank++)
        {
            var person = people - rank;
            var nearest = Candidate(person, rank / 4 + 1, rank % 4 + 1);
            var farther = Candidate(person, nearest.Distance + 1, 0);
            var offset = (people - rank - 1) * occurrences;

            // WHY: Repeated nearest references are separate evidence occurrences with no transport-array copy.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var occurrence = 0; occurrence < occurrences; occurrence++)
            {
                result[offset + occurrence] = occurrence == 0 ? farther : nearest;
            }
        }

        return result;
    }

    /// <summary>Creates pure scalar evidence without involving catalog or database reads.</summary>
    private static SupervisorCandidate Candidate(
        int person,
        int distance,
        int priority
    ) => new()
    {
        SourceUserId = 42,
        StartGroupId = 7,
        FoundGroupId = 20_000 - distance,
        CandidateUserId = person,
        Distance = distance,
        LeadershipPriority = priority,
    };
}

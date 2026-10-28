namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

/// <summary>Checks output-proportional person selection without copying an already complete winner bag.</summary>
/// <param name="output">Receives observed allocations outside the measured operation.</param>
[Trait("Category", "Allocation")]
public sealed class CandidateReuseSelectionTests(ITestOutputHelper output)
{
    /// <summary>Repeated evidence for one winner needs no second occurrence-sized reference array.</summary>
    /// <param name="priority">Whether continuation first prefers business priority over distance.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompleteWinnerEvidenceAvoidsOccurrenceSizedCopies(bool priority)
    {
        // Arrange
        const int occurrences = 131_072;
        var candidates = Enumerable.Range(0, occurrences).Select(index => Candidate(21,
            index == 0 ? 1 : 2, index == 0 ? 10 : 1)).ToArray();

        var selection = priority ? SupervisorSelection.HighestBusinessPriority : SupervisorSelection.RequireUnique;
        for (var iteration = 0; iteration < 4; iteration++)
        {
            _ = SupervisorSelectionRules.SelectOwned(7, candidates, selection);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        // Act
        var result = SupervisorSelectionRules.SelectOwned(7, candidates, selection);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        Assert.Equal(SupervisorResolutionStatus.Found, result.Status);
        Assert.Equal(candidates, result.Evidence);
        Assert.Same(candidates[priority ? 1 : 0], result.Continuation);
        // WHY: The input already contains the complete winning person's evidence. A second 131072-reference
        // buffer cannot fit this fixed allowance, which still admits the result and small selection state.
        Assert.InRange(allocated, 0, 8_192);
        output.WriteLine($"Priority={priority}; occurrences={occurrences}; selection allocation={allocated} B.");
        GC.KeepAlive(result);
    }

    /// <summary>A later strictly better priority clears an earlier tie while retaining the winner's worse rows.</summary>
    /// <param name="reverse">Whether the lowest-priority occurrence arrives before the competing tied people.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LowerPriorityResolvesEarlierAmbiguityWithoutDiscardingWinnerEvidence(bool reverse)
    {
        // Arrange
        SupervisorCandidate[] rows =
        [
            Candidate(10, 1, 10), Candidate(20, 1, 10), Candidate(30, 4, 5),
            Candidate(30, 2, 12), Candidate(30, 3, 5), Candidate(30, 3, 5),
        ];

        var candidates = reverse ? rows.Reverse().ToArray() : rows;
        var expectedEvidence = candidates.Where(row => row.CandidateUserId == 30).ToArray();
        var expectedCursor = expectedEvidence.First(row => row.Distance == 3 && row.LeadershipPriority == 5);

        // Act
        var result = SupervisorSelectionRules.Select(7, candidates, SupervisorSelection.HighestBusinessPriority);

        // Assert
        Assert.Equal(SupervisorResolutionStatus.Found, result.Status);
        Assert.Equal(expectedEvidence, result.Evidence);
        Assert.Same(expectedCursor, result.Continuation);
        Assert.Contains(result.Evidence, row => row.LeadershipPriority == 12);
    }

    /// <summary>A different person at the final best priority remains ambiguous in either arrival order.</summary>
    /// <param name="reverse">Whether the same complete evidence bag arrives in reverse order.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FinalPriorityTieNeverSelectsAnIdentity(bool reverse)
    {
        // Arrange
        SupervisorCandidate[] rows =
        [
            Candidate(99, 4, 10), Candidate(21, 2, 5), Candidate(99, 3, 5), Candidate(21, 1, 7),
        ];

        var candidates = reverse ? rows.Reverse().ToArray() : rows;

        // Act
        var result = SupervisorSelectionRules.Select(7, candidates, SupervisorSelection.HighestBusinessPriority);

        // Assert
        Assert.Equal(SupervisorResolutionStatus.Ambiguous, result.Status);
        Assert.Empty(result.Evidence);
        Assert.Null(result.Continuation);
    }

    /// <summary>Ordinary selection retains its evidence snapshot when the caller later replaces input slots.</summary>
    /// <param name="priority">Whether the selected person is chosen by explicit business priority.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CallerOwnedInputCannotMutateSelectedEvidence(bool priority)
    {
        // Arrange
        var first = Candidate(21, 1, 10);
        var second = Candidate(21, 2, 1);
        SupervisorCandidate[] candidates = [first, second];
        var selection = priority ? SupervisorSelection.HighestBusinessPriority : SupervisorSelection.RequireUnique;

        // Act
        var result = SupervisorSelectionRules.Select(7, candidates, selection);
        candidates[0] = Candidate(99, 1, 0);

        // Assert
        Assert.Equal(new[] { first, second }, result.Evidence);
        Assert.Same(priority ? second : first, result.Continuation);
        Assert.DoesNotContain(result.Evidence, row => row.CandidateUserId == 99);
    }

    /// <summary>Complete outcome bags match an independent grouped reduction across repeated and tied people.</summary>
    /// <param name="mode">The unique, priority, or distinct-person slot contract.</param>
    [Theory]
    [InlineData("unique")]
    [InlineData("priority")]
    [InlineData("first")]
    [InlineData("second")]
    [InlineData("third")]
    [InlineData("missing")]
    public void OnePassSelectionMatchesIndependentGroupedReference(string mode)
    {
        // Arrange
        var selection = mode switch
        {
            "unique" => SupervisorSelection.RequireUnique,
            "priority" => SupervisorSelection.HighestBusinessPriority,
            "first" => SupervisorSelection.NthPerson(1),
            "second" => SupervisorSelection.NthPerson(2),
            "third" => SupervisorSelection.NthPerson(3),
            _ => SupervisorSelection.NthPerson(9),
        };

        var cases = Enumerable.Range(0, 256).Select(seed => Enumerable.Range(0, seed % 9)
                .Select(index => Candidate((seed >> (index % 4)) % 4 - 1,
                    (seed + index * 3) % 5, (seed * 3 + index * 5) % 7 - 3)).ToArray())
            .SelectMany(rows => new[] { rows, rows.Reverse().ToArray() }).ToArray();

        var expected = cases.Select(rows => GroupedReference(rows, selection)).ToArray();

        // Act
        var actual = cases.Select(rows => SupervisorSelectionRules.Select(7, rows, selection)).ToArray();

        // Assert
        Assert.Equal(expected.Length, actual.Length);
        Assert.All(Enumerable.Range(0, actual.Length), index =>
        {
            Assert.Equal(expected[index].Status, actual[index].Status);
            Assert.Equal(expected[index].Evidence, actual[index].Evidence);
            Assert.Same(expected[index].Continuation, actual[index].Continuation);
        });
    }

    /// <summary>Uses grouped LINQ and explicit ordering as an independent, deliberately unoptimized oracle.</summary>
    private static SupervisorResolution GroupedReference(
        SupervisorCandidate[] candidates,
        SupervisorSelection selection
    )
    {
        var people = candidates.GroupBy(row => row.CandidateUserId)
            .Select(group => group.OrderBy(row => row.Distance).ThenBy(row => row.LeadershipPriority).First())
            .ToArray();

        IEnumerable<SupervisorCandidate> pool = people;
        if (selection.Kind == SupervisorSelectionKind.HighestBusinessPriority && candidates.Length != 0)
        {
            var priority = candidates.Min(row => row.LeadershipPriority);
            pool = candidates.Where(row => row.LeadershipPriority == priority)
                .GroupBy(row => row.CandidateUserId).Select(group => group.First());
        }
        else if (selection.Kind == SupervisorSelectionKind.NthPerson)
        {
            var ordered = people.OrderBy(row => row.Distance).ThenBy(row => row.LeadershipPriority).ToArray();
            var slot = selection.Number!.Value - 1;
            pool = slot >= ordered.Length ? [] : ordered.Where(row => row.Distance == ordered[slot].Distance
                && row.LeadershipPriority == ordered[slot].LeadershipPriority);
        }

        var selected = pool.ToArray();
        if (selected.Length != 1)
        {
            return new(selected.Length == 0 ? SupervisorResolutionStatus.NotFound : SupervisorResolutionStatus.Ambiguous,
                7, [], null);
        }

        var evidence = candidates.Where(row => row.CandidateUserId == selected[0].CandidateUserId).ToArray();
        var cursor = selection.Kind == SupervisorSelectionKind.HighestBusinessPriority
            ? evidence.OrderBy(row => row.LeadershipPriority).ThenBy(row => row.Distance).First()
            : evidence.OrderBy(row => row.Distance).First();

        return new(SupervisorResolutionStatus.Found, 7, evidence, cursor);
    }

    /// <summary>Creates one scalar occurrence without coupling pure selection to a database fixture.</summary>
    private static SupervisorCandidate Candidate(int person, int distance, int priority)
        => new()
        {
            SourceUserId = 42, StartGroupId = 7, FoundGroupId = 100 - distance,
            CandidateUserId = person, Distance = distance, LeadershipPriority = priority,
        };
}

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

/// <summary>Checks person-rank semantics and storage proportional to people rather than repeated evidence.</summary>
/// <param name="output">Receives the measured allocation count outside the measured operation.</param>
public sealed class SupervisorPersonRankScaleTests(ITestOutputHelper output)
{
    /// <summary>Ranking uses the best nearest priority without replacing the first nearest continuation.</summary>
    /// <param name="reverse">Whether canonical evidence arrives in the opposite order.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CanonicalPersonRankAndContinuationUseTheirDistinctContracts(bool reverse)
    {
        // Arrange
        SupervisorCandidate[] rows =
        [
            Candidate(90, 2, 9),
            Candidate(90, 2, 2),
            Candidate(90, 4, 0),
            Candidate(80, 2, 4),
        ];

        var candidates = reverse ? rows.Reverse().ToArray() : rows;
        var expectedEvidence = candidates.Where(row => row.CandidateUserId == 90).ToArray();
        var expectedContinuation = expectedEvidence.First(row => row.Distance == 2);

        // Act
        var result = SupervisorSelectionRules.Select(7, candidates, SupervisorSelection.NthPerson(1));

        // Assert
        Assert.Equal(SupervisorResolutionStatus.Found, result.Status);
        Assert.Equal(expectedEvidence, result.Evidence);
        Assert.Same(expectedContinuation, result.Continuation);
    }

    /// <summary>Each slot occupied by a multi-person rank band stays ambiguous despite repeated evidence.</summary>
    /// <param name="slot">A slot within the four-person tied rank band.</param>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void EverySlotInsideLargerRankBandIsAmbiguous(int slot)
    {
        // Arrange
        SupervisorCandidate[] rows =
        [
            Candidate(50, 1, 1),
            Candidate(60, 2, 2),
            Candidate(10, 2, 2),
            Candidate(90, 2, 2),
            Candidate(30, 2, 2),
            Candidate(80, 3, 1),
        ];

        var candidates = rows.Concat(rows).ToArray();

        // Act
        var result = SupervisorSelectionRules.Select(7, candidates, SupervisorSelection.NthPerson(slot));

        // Assert
        Assert.Equal(SupervisorResolutionStatus.Ambiguous, result.Status);
        Assert.Empty(result.Evidence);
        Assert.Null(result.Continuation);
    }

    /// <summary>Large fan-in ranks distinct people independently of input order, IDs, or duplicate evidence.</summary>
    /// <param name="slot">The one-based requested slot, including a slot beyond every distinct person.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(5_001)]
    [InlineData(10_000)]
    [InlineData(10_001)]
    public void LargeFanInUsesCanonicalDistinctPersonSlots(int slot)
    {
        // Arrange
        const int people = 10_000;
        const int occurrencesPerPerson = 8;
        var candidates = RankedEvidence(people, occurrencesPerPerson);
        var expectedPerson = people - slot + 1;
        var expectedEvidence = candidates.Where(row => row.CandidateUserId == expectedPerson).ToArray();
        var expectedStatus = slot <= people
            ? SupervisorResolutionStatus.Found : SupervisorResolutionStatus.NotFound;

        var expectedContinuation = expectedEvidence.Length == 0 ? null : expectedEvidence[1];

        // Act
        var result = SupervisorSelectionRules.Select(7, candidates, SupervisorSelection.NthPerson(slot));

        // Assert
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(7, result.StartGroupId);
        Assert.Equal(expectedEvidence, result.Evidence);
        Assert.Same(expectedContinuation, result.Continuation);
    }

    /// <summary>An evaluated origin without people has no occupied first slot.</summary>
    [Fact]
    public void EmptyPersonRankIsNotFound()
    {
        // Arrange
        SupervisorCandidate[] candidates = [];

        // Act
        var result = SupervisorSelectionRules.Select(7, candidates, SupervisorSelection.NthPerson(1));

        // Assert
        Assert.Equal(SupervisorResolutionStatus.NotFound, result.Status);
        Assert.Equal(7, result.StartGroupId);
        Assert.Empty(result.Evidence);
        Assert.Null(result.Continuation);
    }

    /// <summary>Repeated evidence does not require per-person occurrence buckets or a sorted occurrence copy.</summary>
    [Trait("Category", "Allocation")]
    [Fact]
    public void RankStorageIsBoundedByDistinctPeopleInsteadOfOccurrenceFanIn()
    {
        // Arrange
        const int people = 1_024;
        const int occurrencesPerPerson = 128;
        var candidates = RankedEvidence(people, occurrencesPerPerson);
        var selection = SupervisorSelection.NthPerson(513);

        for (var iteration = 0; iteration < 4; iteration++)
        {
            _ = SupervisorSelectionRules.Select(7, candidates, selection);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        // Act
        var result = SupervisorSelectionRules.Select(7, candidates, selection);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        output.WriteLine($"People={people}; occurrences={candidates.Length}; rank allocation={allocated} B.");
        Assert.Equal(SupervisorResolutionStatus.Found, result.Status);
        Assert.Equal(occurrencesPerPerson, result.Evidence.Count);
        Assert.Equal(512, result.Continuation!.CandidateUserId);

        // WHY: The budget allows dictionary growth, one rank reference per person, and selected evidence.
        // A bucket or sorted copy for all 131072 original occurrences cannot fit within this storage bound.
        Assert.InRange(allocated, 0, people * 128L + 16_384);
        GC.KeepAlive(result);
    }

    /// <summary>Creates reversed person IDs with independently known unique distance/priority rank slots.</summary>
    private static SupervisorCandidate[] RankedEvidence(int people, int occurrencesPerPerson)
    {
        var result = new SupervisorCandidate[people * occurrencesPerPerson];

        for (var rank = 0; rank < people; rank++)
        {
            var person = people - rank;
            var nearest = Candidate(person, rank / 4 + 1, rank % 4 + 1);
            var farther = Candidate(person, nearest.Distance + 1, 0);
            var offset = (people - rank - 1) * occurrencesPerPerson;

            for (var occurrence = 0; occurrence < occurrencesPerPerson; occurrence++)
            {
                // WHY: Repeated nearest references are separate evidence occurrences; the earlier farther
                // occurrence has a better priority but cannot define this person's nearest rank or cursor.
                result[offset + occurrence] = occurrence == 0 ? farther : nearest;
            }
        }

        return result;
    }

    /// <summary>Builds pure scalar evidence without coupling rank tests to provider or context setup.</summary>
    private static SupervisorCandidate Candidate(int person, int distance, int priority) => new()
    {
        TenantId = new Guid("dddddddd-dddd-dddd-dddd-dddddddddddd"),
        SourceUserId = 42,
        StartGroupId = 7,
        StartTreeId = new Guid("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
        FoundGroupId = 20_000 - distance,
        CandidateUserId = person,
        Distance = distance,
        LeadershipPriority = priority,
    };
}

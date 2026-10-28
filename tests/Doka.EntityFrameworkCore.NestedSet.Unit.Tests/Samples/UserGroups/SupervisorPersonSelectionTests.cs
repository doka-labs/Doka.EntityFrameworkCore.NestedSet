namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

/// <summary>Checks person uniqueness, tied rank slots, and the continuation chosen from retained evidence.</summary>
public sealed class SupervisorPersonSelectionTests
{
    /// <summary>
    /// Person uniqueness distinguishes no match, one person, and multiple people without an ID tie-breaker.
    /// </summary>
    /// <param name="count">The number of distinct candidate people.</param>
    /// <param name="expected">The expected person-resolution status.</param>
    [Theory]
    [InlineData(0, "NotFound")]
    [InlineData(1, "Found")]
    [InlineData(2, "Ambiguous")]
    public void UniqueSelectionDistinguishesMissingAndAmbiguous(
        int count,
        string expected
    )
    {
        // Arrange
        var candidates = Enumerable.Range(1, count).Select(person => Candidate(person, 1, 10)).ToArray();

        // Act
        var result = SupervisorSelectionRules.Select(7, candidates, SupervisorSelection.RequireUnique);

        // Assert
        Assert.Equal(expected, result.Status.ToString());
        Assert.Equal(7, result.StartGroupId);
        Assert.Equal(count == 1, result.Continuation is not null);
    }

    /// <summary>All preserves each candidate occurrence and does not claim a canonical unique continuation.</summary>
    [Fact]
    public void AllPreservesOccurrencesForRepeatedPeople()
    {
        // Arrange
        SupervisorCandidate[] candidates = [Candidate(21, 1, 9), Candidate(21, 3, 2), Candidate(22, 2, 4)];

        // Act
        var result = SupervisorSelectionRules.Select(7, candidates, SupervisorSelection.All);

        // Assert
        Assert.Equal(SupervisorResolutionStatus.Found, result.Status);
        Assert.Same(candidates, result.Evidence);
        Assert.Null(result.Continuation);
    }

    /// <summary>Explicitly requesting all candidates still reports an evaluated origin with no matches.</summary>
    [Fact]
    public void AllWithoutCandidatesIsNotFound()
    {
        // Arrange
        SupervisorCandidate[] candidates = [];

        // Act
        var result = SupervisorSelectionRules.Select(7, candidates, SupervisorSelection.All);

        // Assert
        Assert.Equal(SupervisorResolutionStatus.NotFound, result.Status);
        Assert.Empty(result.Evidence);
        Assert.Null(result.Continuation);
    }

    /// <summary>Evidence for the same person remains visible and does not create ambiguity.</summary>
    [Fact]
    public void RepeatedEvidenceForSamePersonIsNotAmbiguous()
    {
        // Arrange
        SupervisorCandidate[] candidates = [Candidate(21, 3, 1), Candidate(21, 1, 9)];

        // Act
        var result = SupervisorSelectionRules.Select(7, candidates, SupervisorSelection.RequireUnique);

        // Assert
        Assert.Equal(SupervisorResolutionStatus.Found, result.Status);
        Assert.Equal(candidates, result.Evidence);
        Assert.Same(candidates[1], result.Continuation);
    }

    /// <summary>
    /// Business priority can select a farther person instead of silently applying nearest-distance ranking.
    /// </summary>
    [Fact]
    public void SmallestBusinessPriorityWinsAcrossDistances()
    {
        // Arrange
        SupervisorCandidate[] candidates = [Candidate(10, 1, 5), Candidate(20, 4, 1), Candidate(30, 2, 7)];

        // Act
        var result = SupervisorSelectionRules.Select(7, candidates, SupervisorSelection.HighestBusinessPriority);

        // Assert
        Assert.Equal(SupervisorResolutionStatus.Found, result.Status);
        Assert.Equal(20, Assert.Single(result.Evidence).CandidateUserId);
        Assert.Equal(4, result.Continuation!.Distance);
    }

    /// <summary>Equal best priorities across distinct people stay ambiguous even when their distances differ.</summary>
    [Fact]
    public void EqualBestBusinessPriorityDoesNotChooseTechnicalIdentity()
    {
        // Arrange
        SupervisorCandidate[] candidates = [Candidate(1, 1, 2), Candidate(99, 4, 2), Candidate(3, 2, 8)];

        // Act
        var result = SupervisorSelectionRules.Select(7, candidates, SupervisorSelection.HighestBusinessPriority);

        // Assert
        Assert.Equal(SupervisorResolutionStatus.Ambiguous, result.Status);
        Assert.Null(result.Continuation);
    }

    /// <summary>
    /// Repeated best-priority evidence for one person remains unique and uses its nearest best occurrence.
    /// </summary>
    [Fact]
    public void BusinessPriorityUsesBestThenNearestOccurrence()
    {
        // Arrange
        SupervisorCandidate[] candidates =
        [
            Candidate(21, 1, 8),
            Candidate(21, 5, 2),
            Candidate(21, 3, 2),
            Candidate(22, 2, 5),
        ];

        // Act
        var result = SupervisorSelectionRules.Select(7, candidates, SupervisorSelection.HighestBusinessPriority);

        // Assert
        Assert.Equal(SupervisorResolutionStatus.Found, result.Status);
        Assert.Equal(3, result.Evidence.Count);
        Assert.All(result.Evidence, row => Assert.Equal(21, row.CandidateUserId));
        Assert.Same(candidates[2], result.Continuation);
    }

    /// <summary>
    /// Two equal people occupy both tied slots; the following person occupies slot three rather than dense rank two.
    /// </summary>
    /// <param name="slot">The requested one-based person slot, including tied slots.</param>
    /// <param name="expected">The expected resolution status for that slot.</param>
    /// <param name="person">The unique candidate identity when the slot is resolved.</param>
    [Theory]
    [InlineData(1, "Ambiguous", null)]
    [InlineData(2, "Ambiguous", null)]
    [InlineData(3, "Found", 30)]
    [InlineData(4, "NotFound", null)]
    public void PersonRankRetainsEveryTiedSlot(
        int slot,
        string expected,
        int? person
    )
    {
        // Arrange
        SupervisorCandidate[] candidates =
        [
            Candidate(10, 1, 2),
            Candidate(20, 1, 2),
            Candidate(10, 3, 1),
            Candidate(30, 2, 1),
        ];

        // Act
        var result = SupervisorSelectionRules.Select(7, candidates, SupervisorSelection.NthPerson(slot));

        // Assert
        Assert.Equal(expected, result.Status.ToString());
        Assert.Equal(person, result.Continuation?.CandidateUserId);
    }

    /// <summary>
    /// Person ranking uses each person's nearest occurrence before applying that occurrence's business priority.
    /// </summary>
    [Fact]
    public void FartherBetterPriorityDoesNotReplaceNearestOccurrenceForPersonRank()
    {
        // Arrange
        SupervisorCandidate[] candidates = [Candidate(10, 1, 8), Candidate(10, 4, 1), Candidate(20, 1, 3)];

        // Act
        var result = SupervisorSelectionRules.Select(7, candidates, SupervisorSelection.NthPerson(1));

        // Assert
        Assert.Equal(SupervisorResolutionStatus.Found, result.Status);
        Assert.Equal(20, result.Continuation!.CandidateUserId);
    }

    /// <summary>
    /// Uniqueness and person ranking keep all evidence while continuing from the nearest matching group.
    /// </summary>
    /// <param name="personRank">Whether the test uses person ranking instead of explicit uniqueness.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UniquePersonHasNearestDeterministicContinuation(
        bool personRank
    )
    {
        // Arrange
        SupervisorCandidate[] candidates = [Candidate(21, 4, 1), Candidate(21, 1, 9), Candidate(21, 2, 4)];
        var selection = personRank ? SupervisorSelection.NthPerson(1) : SupervisorSelection.RequireUnique;

        // Act
        var result = SupervisorSelectionRules.Select(7, candidates, selection);

        // Assert
        Assert.Equal(SupervisorResolutionStatus.Found, result.Status);
        Assert.Equal(candidates, result.Evidence);
        Assert.Same(candidates[1], result.Continuation);
    }

    /// <summary>
    /// Builds evidence on one valid origin path, where each distance corresponds to one distinct group.
    /// </summary>
    /// <param name="person">The candidate user identity.</param>
    /// <param name="distance">The structural distance of this occurrence from the origin.</param>
    /// <param name="priority">The business priority stored on this candidate's membership.</param>
    private static SupervisorCandidate Candidate(
        int person,
        int distance,
        int priority
    ) => new()
    {
        TenantId = new Guid("dddddddd-dddd-dddd-dddd-dddddddddddd"),
        SourceUserId = 42,
        StartGroupId = 7,
        StartTreeId = new Guid("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
        FoundGroupId = 100 - distance,
        CandidateUserId = person,
        Distance = distance,
        LeadershipPriority = priority,
    };
}

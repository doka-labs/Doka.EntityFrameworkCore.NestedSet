namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

/// <summary>
/// Checks explicit cross-origin person reduction without discarding provenance or inventing fallback.
/// </summary>
public sealed class SupervisorOriginSelectionTests
{
    /// <summary>
    /// The same person on separate paths remains one person with all origins and no invented single cursor.
    /// </summary>
    [Fact]
    public void GlobalUniquePersonRetainsEverySupportingOrigin()
    {
        // Arrange
        var first = Candidate(7, 21);
        var second = Candidate(9, 21);
        var batch = Batch(Found(7, first), Found(9, second));

        // Act
        var result = SupervisorSelectionRules.Across(batch, new RequireUniquePerson());

        // Assert
        Assert.Equal(SupervisorResolutionStatus.Found, result.Status);
        Assert.Equal([first, second], result.Evidence);
        Assert.Null(result.StartGroupId);
        Assert.Null(result.Continuation);
    }

    /// <summary>
    /// Global reduction distinguishes no candidates, one person, and multiple people across evaluated origins.
    /// </summary>
    /// <param name="people">The distinct people represented by all evaluated origins.</param>
    /// <param name="expected">The expected global resolution status.</param>
    [Theory]
    [InlineData(0, "NotFound")]
    [InlineData(1, "Found")]
    [InlineData(2, "Ambiguous")]
    public void GlobalSelectionDistinguishesPersonCardinality(
        int people,
        string expected
    )
    {
        // Arrange
        var candidates = Enumerable.Range(1, people).Select(person => Candidate(7, person)).ToArray();
        var batch = Batch(people == 0 ? Missing(7) : Found(7, candidates), Missing(9));

        // Act
        var result = SupervisorSelectionRules.Across(batch, new RequireUniquePerson());

        // Assert
        Assert.Equal(expected, result.Status.ToString());
        Assert.Null(result.StartGroupId);
        Assert.Null(result.Continuation);
    }

    /// <summary>An ambiguous evaluated path blocks a falsely unique global answer from another origin.</summary>
    [Fact]
    public void AmbiguousOriginCannotBeHiddenByAnotherUniqueOrigin()
    {
        // Arrange
        var batch = Batch(
            new SupervisorResolution(SupervisorResolutionStatus.Ambiguous, 7, [], null),
            Found(9, Candidate(9, 21)));

        // Act
        var result = SupervisorSelectionRules.Across(batch, new RequireUniquePerson());

        // Assert
        Assert.Equal(SupervisorResolutionStatus.Ambiguous, result.Status);
        Assert.Null(result.Continuation);
    }

    /// <summary>An explicit origin order overrides collection order and technical group identities.</summary>
    [Fact]
    public void PrioritizedOriginUsesTheApplicationOrder()
    {
        // Arrange
        var selected = Candidate(99, 21);
        var batch = Batch(Found(1, Candidate(1, 22)), Found(99, selected));

        // Act
        var result = SupervisorSelectionRules.Across(batch, new FromPrioritizedOrigin([99, 1]));

        // Assert
        Assert.Equal(SupervisorResolutionStatus.Found, result.Status);
        Assert.Equal(99, result.StartGroupId);
        Assert.Same(selected, result.Continuation);
    }

    /// <summary>
    /// A preferred group absent from the evaluated own memberships does not block the next allowed origin.
    /// </summary>
    [Fact]
    public void PrioritizedOriginSkipsAbsentMemberships()
    {
        // Arrange
        var selected = Candidate(9, 21);
        var batch = Batch(Found(9, selected));

        // Act
        var result = SupervisorSelectionRules.Across(batch, new FromPrioritizedOrigin([7, 9]));

        // Assert
        Assert.Equal(SupervisorResolutionStatus.Found, result.Status);
        Assert.Equal(9, result.StartGroupId);
        Assert.Same(selected, result.Continuation);
    }

    /// <summary>
    /// A selected present origin retains its missing or ambiguous outcome instead of falling back to a later match.
    /// </summary>
    /// <param name="status">The unresolved status of the preferred present origin.</param>
    [Theory]
    [InlineData("NotFound")]
    [InlineData("Ambiguous")]
    public void PreferredPresentOriginDoesNotFallBackAfterFailure(string status)
    {
        // Arrange
        var first = new SupervisorResolution(Enum.Parse<SupervisorResolutionStatus>(status), 7, [], null);
        var batch = Batch(first, Found(9, Candidate(9, 21)));

        // Act
        var result = SupervisorSelectionRules.Across(batch, new FromPrioritizedOrigin([7, 9]));

        // Assert
        Assert.Same(first, result);
        Assert.Null(result.Continuation);
    }

    /// <summary>All is not a unique-person proof: multiple people in the preferred origin stay ambiguous.</summary>
    [Fact]
    public void PreferredAllOriginStillRequiresOneDistinctPerson()
    {
        // Arrange
        var batch = Batch(Found(7, Candidate(7, 21), Candidate(7, 22)), Found(9, Candidate(9, 23)));

        // Act
        var result = SupervisorSelectionRules.Across(batch, new FromPrioritizedOrigin([7, 9]));

        // Assert
        Assert.Equal(SupervisorResolutionStatus.Ambiguous, result.Status);
        Assert.Equal(7, result.StartGroupId);
        Assert.Null(result.Continuation);
    }

    /// <summary>
    /// Repeated evidence in a preferred All origin stays unique and retains its nearest continuation.
    /// </summary>
    [Fact]
    public void PreferredAllOriginDeduplicatesPeopleAndRetainsOccurrences()
    {
        // Arrange
        var near = Candidate(7, 21);
        var far = near with
        {
            Distance = 3,
            FoundGroupId = 2,
        };

        var batch = Batch(Found(7, far, near), Found(9, Candidate(9, 22)));

        // Act
        var result = SupervisorSelectionRules.Across(batch, new FromPrioritizedOrigin([7, 9]));

        // Assert
        Assert.Equal(SupervisorResolutionStatus.Found, result.Status);
        Assert.Equal([far, near], result.Evidence);
        Assert.Same(near, result.Continuation);
    }

    /// <summary>
    /// Incomplete reader output never establishes uniqueness, including an otherwise unique preferred origin.
    /// </summary>
    /// <param name="prioritize">Whether the reduction chooses an explicit preferred origin.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncompleteBatchIsNeverReducedToSuccessfulUniqueness(bool prioritize)
    {
        // Arrange
        var batch = Batch(Found(7, Candidate(7, 21))) with { Complete = false };
        AcrossOriginsSelection selection = prioritize ? new FromPrioritizedOrigin([7]) : new RequireUniquePerson();

        // Act
        var result = SupervisorSelectionRules.Across(batch, selection);

        // Assert
        Assert.Equal(SupervisorResolutionStatus.LimitExceeded, result.Status);
        Assert.Empty(result.Evidence);
        Assert.Null(result.Continuation);
    }

    /// <summary>Request-level absence remains distinct from a completed origin that found no supervisor.</summary>
    /// <param name="status">The batch-level source or membership absence status.</param>
    [Theory]
    [InlineData("SourceNotFound")]
    [InlineData("NoVisibleMemberships")]
    public void MissingRequestStatusIsPreserved(string status)
    {
        // Arrange
        var batch = new SupervisorResolutionBatch(Enum.Parse<SupervisorBatchStatus>(status), []);

        // Act
        var result = SupervisorSelectionRules.Across(batch, new RequireUniquePerson());

        // Assert
        Assert.Equal(status, result.Status.ToString());
        Assert.Empty(result.Evidence);
        Assert.Null(result.StartGroupId);
    }

    /// <summary>An origin priority permits only explicitly listed own memberships.</summary>
    [Fact]
    public void NoAllowedOwnMembershipIsNotANotFoundPerson()
    {
        // Arrange
        var batch = Batch(Found(7, Candidate(7, 21)));

        // Act
        var result = SupervisorSelectionRules.Across(batch, new FromPrioritizedOrigin([99]));

        // Assert
        Assert.Equal(SupervisorResolutionStatus.NoVisibleMemberships, result.Status);
        Assert.Empty(result.Evidence);
    }

    /// <summary>Empty or repeated priority entries do not define an unambiguous application origin order.</summary>
    /// <param name="repeated">Whether the invalid priority repeats an origin instead of omitting all origins.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidOriginPriorityIsRejected(bool repeated)
    {
        // Arrange
        var batch = Batch(Found(7, Candidate(7, 21)));
        var selection = new FromPrioritizedOrigin(repeated ? [7, 7] : []);

        // Act
        var error = Record.Exception(() => SupervisorSelectionRules.Across(batch, selection));

        // Assert
        Assert.IsType<ArgumentException>(error);
    }

    /// <summary>A null origin priority does not stand for all visible memberships.</summary>
    [Fact]
    public void MissingOriginPriorityIsRejected()
    {
        // Arrange
        var batch = Batch(Found(7, Candidate(7, 21)));
        var selection = new FromPrioritizedOrigin(null!);

        // Act
        var error = Record.Exception(() => SupervisorSelectionRules.Across(batch, selection));

        // Assert
        Assert.IsType<ArgumentNullException>(error);
    }

    /// <summary>Unrecognized origin selection cannot silently act like global uniqueness.</summary>
    [Fact]
    public void UnknownOriginSelectionIsRejected()
    {
        // Arrange
        var batch = Batch(Found(7, Candidate(7, 21)));

        // Act
        var error = Record.Exception(() => SupervisorSelectionRules.Across(batch, new UnknownSelection()));

        // Assert
        Assert.IsType<ArgumentException>(error);
    }

    /// <summary>
    /// Origin prioritization preserves a previously selected business-priority continuation occurrence.
    /// </summary>
    [Fact]
    public void PrioritizedOriginPreservesTheSelectedContinuationOccurrence()
    {
        // Arrange
        var nearest = Candidate(7, 21) with
        {
            Distance = 1,
            LeadershipPriority = 10,
            FoundGroupId = 6,
        };

        var best = nearest with
        {
            Distance = 3,
            LeadershipPriority = 1,
            FoundGroupId = 4,
        };

        var origin = new SupervisorResolution(SupervisorResolutionStatus.Found, 7, [nearest, best], best);
        var batch = Batch(origin);

        // Act
        var selected = SupervisorSelectionRules.Across(batch, new FromPrioritizedOrigin([7]));

        // Assert
        Assert.Equal(SupervisorResolutionStatus.Found, selected.Status);
        Assert.Equal(best, selected.Continuation);
        Assert.Equal(new[] { nearest, best }, selected.Evidence);
    }

    /// <summary>An invalid origin priority is rejected even when the source does not exist.</summary>
    [Fact]
    public void InvalidPriorityIsRejectedBeforeSourceAbsenceIsReduced()
    {
        // Arrange
        var batch = new SupervisorResolutionBatch(SupervisorBatchStatus.SourceNotFound, []);
        var selection = new FromPrioritizedOrigin([]);

        // Act
        var error = Record.Exception(() => SupervisorSelectionRules.Across(batch, selection));

        // Assert
        Assert.IsType<ArgumentException>(error);
    }

    /// <summary>
    /// A limited origin cannot become a unique global person even when the batch flag is inconsistent.
    /// </summary>
    [Fact]
    public void LimitedOriginCannotBecomeAUniquePerson()
    {
        // Arrange
        var limited = new SupervisorResolution(SupervisorResolutionStatus.LimitExceeded, 8, [], null);
        var batch = Batch(Found(7, Candidate(7, 21)), limited);

        // Act
        var selected = SupervisorSelectionRules.Across(batch, new RequireUniquePerson());

        // Assert
        Assert.Equal(SupervisorResolutionStatus.LimitExceeded, selected.Status);
        Assert.Empty(selected.Evidence);
    }

    /// <summary>Constructs a completed batch with every explicitly evaluated origin.</summary>
    /// <param name="origins">The origins retained in their explicit application order.</param>
    private static SupervisorResolutionBatch Batch(params SupervisorResolution[] origins)
        => new(SupervisorBatchStatus.Completed, origins);

    /// <summary>Constructs an All-style found origin without asserting unique person selection in advance.</summary>
    /// <param name="group">The own-membership origin group.</param>
    /// <param name="evidence">Every candidate occurrence retained for this origin.</param>
    private static SupervisorResolution Found(
        int group,
        params SupervisorCandidate[] evidence
    ) => new(SupervisorResolutionStatus.Found, group, evidence, null);

    /// <summary>Constructs an evaluated origin that has no supervisor candidates.</summary>
    /// <param name="group">The evaluated own-membership origin group.</param>
    private static SupervisorResolution Missing(int group)
        => new(SupervisorResolutionStatus.NotFound, group, [], null);

    /// <summary>Builds one candidate occurrence with a complete source identity.</summary>
    /// <param name="group">The own-membership origin group.</param>
    /// <param name="person">The candidate user identity.</param>
    private static SupervisorCandidate Candidate(
        int group,
        int person
    ) => new()
    {
        TenantId = new Guid("dddddddd-dddd-dddd-dddd-dddddddddddd"),
        SourceUserId = 42,
        StartGroupId = group,
        StartTreeId = new Guid("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
        FoundGroupId = group - 1,
        CandidateUserId = person,
        Distance = 1,
        LeadershipPriority = 2,
    };

    /// <summary>Ensures an unsupported alternative is rejected explicitly.</summary>
    private sealed record UnknownSelection : AcrossOriginsSelection;
}

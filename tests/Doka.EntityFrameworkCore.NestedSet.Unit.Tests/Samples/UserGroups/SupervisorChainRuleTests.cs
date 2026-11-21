namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

/// <summary>Checks finite chain requests, per-path cycles, provenance, and safety limits before path growth.</summary>
public sealed partial class SupervisorChainRuleTests
{
    /// <summary>
    /// Original-path requests allow a first self search and require later steps to move strictly upward.
    /// </summary>
    [Fact]
    public void Validate_OriginalPath_AcceptsExplicitFiniteUpwardSteps()
    {
        // Arrange
        var request = Request() with { Steps = [Step(SearchStart.IncludeSelf), Step(SearchStart.ParentsOnly)] };

        // Act
        var error = Record.Exception(() => request.Validate(originalPath: true));

        // Assert
        Assert.Null(error);
    }

    /// <summary>
    /// The re-expanding API independently permits the explicitly requested self boundary at every hop.
    /// </summary>
    [Fact]
    public void Validate_AllMemberships_AcceptsExplicitSelfSearchAtLaterSteps()
    {
        // Arrange
        var request = Request() with { Steps = [Step(SearchStart.IncludeSelf), Step(SearchStart.IncludeSelf)] };

        // Act
        var error = Record.Exception(() => request.Validate(originalPath: false));

        // Assert
        Assert.Null(error);
    }

    /// <summary>The original-path API rejects a later self search rather than silently ignoring its boundary.</summary>
    [Fact]
    public void Validate_OriginalPath_RejectsLaterSelfSearch()
    {
        // Arrange
        var request = Request() with { Steps = [Step(SearchStart.ParentsOnly), Step(SearchStart.IncludeSelf)] };

        // Act
        var error = Record.Exception(() => request.Validate(originalPath: true));

        // Assert
        Assert.IsType<ArgumentException>(error);
    }

    /// <summary>A missing, empty, or malformed finite step list cannot begin a chain.</summary>
    /// <param name="invalid">The malformed finite-step request alternative.</param>
    [Theory]
    [InlineData("empty")]
    [InlineData("null-step")]
    [InlineData("unknown-routing")]
    [InlineData("invalid-person-rank")]
    [InlineData("invalid-group-rank")]
    public void Validate_InvalidChainRule_IsRejected(
        string invalid
    )
    {
        // Arrange
        var request = invalid switch
        {
            "empty" => Request() with { Steps = [] },
            "null-step" => Request() with { Steps = [null!] },
            "unknown-routing" => Request() with { RoutingOrigin = (RoutingOrigin)99 },
            "invalid-person-rank" => Request() with
            {
                Steps = [Step(SearchStart.ParentsOnly) with { Selection = SupervisorSelection.NthPerson(0) }],
            },
            _ => Request() with
            {
                Steps = [Step(SearchStart.ParentsOnly) with { Groups = new NthEligibleGroup([], 0) }],
            },
        };

        // Act
        var error = Record.Exception(() => request.Validate(originalPath: true));

        // Assert
        Assert.IsType<ArgumentException>(error);
    }

    /// <summary>A step without an explicit person-selection rule is rejected as a request error.</summary>
    [Fact]
    public void Validate_MissingStepSelection_IsRejectedAsAnArgumentError()
    {
        // Arrange
        var request = Request() with
        {
            Steps = [Step(SearchStart.ParentsOnly) with { Selection = null! }],
        };

        // Act
        var error = Record.Exception(() => request.Validate(originalPath: true));

        // Assert
        Assert.IsAssignableFrom<ArgumentException>(error);
    }

    /// <summary>A missing step collection cannot begin a finite chain.</summary>
    [Fact]
    public void Validate_MissingSteps_AreRejected()
    {
        // Arrange
        var request = Request() with { Steps = null! };

        // Act
        var error = Record.Exception(() => request.Validate(originalPath: true));

        // Assert
        Assert.IsType<ArgumentNullException>(error);
    }

    /// <summary>
    /// A prior subject or initial subject ends only this path as a cycle and is not appended as a valid hop.
    /// </summary>
    /// <param name="person">The repeated person whose occurrence would introduce a cycle.</param>
    [Theory]
    [InlineData(42)]
    [InlineData(21)]
    public void AdvanceBranch_RepeatedPerson_StopsThePathWithoutAppendingCycleHop(
        int person
    )
    {
        // Arrange
        var previous = Prefix();
        var candidate = Candidate(person, 2, 1);
        var resolution = Found(candidate);
        var budget = new SupervisorQueries.ChainBudget(Request());
        List<SupervisorQueries.ChainPath> next = [];
        List<SupervisorChainBranch> completed = [];

        // Act
        var admitted = SupervisorQueries.AdvanceBranch(
            resolution,
            previous,
            42,
            22,
            7,
            previous.InitialTreeId,
            3,
            SupervisorSelection.RequireUnique,
            budget,
            next,
            completed);

        // Assert
        Assert.True(admitted);
        Assert.Empty(next);
        var branch = Assert.Single(completed);
        Assert.Equal(SupervisorChainStatus.CycleDetected, branch.Status);
        Assert.Equal(7, branch.InitialGroupId);
        Assert.Equal([21, 22], branch.Hops.Select(hop => hop.Evidence.CandidateUserId));
    }

    /// <summary>
    /// A continuation retains its initial origin and immutable prefix when its current search uses another tree.
    /// </summary>
    [Fact]
    public void AdvanceBranch_ContinuedPath_KeepsInitialProvenanceAndNewCurrentEvidence()
    {
        // Arrange
        var previous = Prefix();
        var otherTree = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var candidate = Candidate(23, 11, 10) with { StartTreeId = otherTree };
        var budget = new SupervisorQueries.ChainBudget(Request());
        List<SupervisorQueries.ChainPath> next = [];
        List<SupervisorChainBranch> completed = [];

        // Act
        var admitted = SupervisorQueries.AdvanceBranch(
            Found(candidate),
            previous,
            42,
            22,
            7,
            previous.InitialTreeId,
            3,
            SupervisorSelection.RequireUnique,
            budget,
            next,
            completed);

        // Assert
        Assert.True(admitted);
        Assert.Empty(completed);
        var path = Assert.Single(next);
        Assert.Same(previous, path.Previous);
        Assert.Equal(previous.InitialTreeId, path.InitialTreeId);
        Assert.Equal(7, path.InitialGroupId);
        var hops = path.Materialize();
        Assert.Equal([21, 22, 23], hops.Select(hop => hop.Evidence.CandidateUserId));
        Assert.Equal([1, 2, 3], hops.Select(hop => hop.Number));
        Assert.All(hops, hop => Assert.Equal(7, hop.InitialGroupId));
        Assert.All(hops, hop => Assert.Equal(previous.InitialTreeId, hop.InitialTreeId));
        Assert.Equal(otherTree, hops[2].Evidence.StartTreeId);
    }

    /// <summary>
    /// Missing and ambiguous selections preserve already traversed hops and terminate the evaluated origin.
    /// </summary>
    /// <param name="resolutionStatus">The unresolved outcome returned by the next selection.</param>
    /// <param name="branchStatus">The terminal branch status preserving the successful prefix.</param>
    [Theory]
    [InlineData("NotFound", "NoSupervisor")]
    [InlineData("Ambiguous", "Ambiguous")]
    public void AdvanceBranch_UnresolvedSelection_TerminatesOnlyItsExistingPath(
        string resolutionStatus,
        string branchStatus
    )
    {
        // Arrange
        var previous = Prefix();
        var resolution = new SupervisorResolution(
            Enum.Parse<SupervisorResolutionStatus>(resolutionStatus),
            5,
            [],
            null);

        List<SupervisorQueries.ChainPath> next = [];
        List<SupervisorChainBranch> completed = [];
        var budget = new SupervisorQueries.ChainBudget(Request());

        // Act
        var admitted = SupervisorQueries.AdvanceBranch(
            resolution,
            previous,
            42,
            22,
            7,
            previous.InitialTreeId,
            3,
            SupervisorSelection.RequireUnique,
            budget,
            next,
            completed);

        // Assert
        Assert.True(admitted);
        Assert.Empty(next);
        var branch = Assert.Single(completed);
        Assert.Equal(branchStatus, branch.Status.ToString());
        Assert.Equal([21, 22], branch.Hops.Select(hop => hop.Evidence.CandidateUserId));
    }

    /// <summary>
    /// Creates explicit generous bounds so individual tests can reduce only the boundary under examination.
    /// </summary>
    private static SupervisorChainRequest Request() => new()
    {
        SourceUserId = 42,
        Steps = [Step(SearchStart.ParentsOnly)],
        RoutingOrigin = RoutingOrigin.InitialSubject,
        MaxFrontierStates = 10,
        MaxVisitedStates = 10,
        MaxResultRows = 10,
    };

    /// <summary>Creates one complete explicit hop rule without database or role catalog access.</summary>
    /// <param name="start">The explicit inclusion boundary for this hop's first searched group.</param>
    private static SupervisorStep Step(
        SearchStart start
    )
        => new(start, new NoRoleFilter(), new AllEligibleGroups(), [], null, SupervisorSelection.RequireUnique);

    /// <summary>
    /// Creates a two-hop shared prefix that can be retained without copying before a continuation is admitted.
    /// </summary>
    private static SupervisorQueries.ChainPath Prefix()
    {
        var first = Candidate(21, 7, 6);
        var firstPath = new SupervisorQueries.ChainPath(
            new SupervisorHop(1, 42, 7, first.StartTreeId, first),
            null,
            7,
            first.StartTreeId);

        var second = Candidate(22, 6, 5);

        return new SupervisorQueries.ChainPath(
            new SupervisorHop(2, 42, 7, first.StartTreeId, second),
            firstPath,
            7,
            first.StartTreeId);
    }

    /// <summary>Creates an already unique resolution with its exact continuation occurrence.</summary>
    /// <param name="candidate">The unique occurrence retained as both evidence and continuation.</param>
    private static SupervisorResolution Found(
        SupervisorCandidate candidate
    )
        => new(SupervisorResolutionStatus.Found, candidate.StartGroupId, [candidate], candidate);

    /// <summary>Creates a valid occurrence with exact current search provenance.</summary>
    /// <param name="person">The selected candidate user.</param>
    /// <param name="start">The current search's own-membership origin group.</param>
    /// <param name="found">The ancestor group supplying the candidate occurrence.</param>
    private static SupervisorCandidate Candidate(
        int person,
        int start,
        int found
    ) => new()
    {
        TenantId = new Guid("dddddddd-dddd-dddd-dddd-dddddddddddd"),
        SourceUserId = 42,
        StartGroupId = start,
        StartTreeId = new Guid("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
        FoundGroupId = found,
        CandidateUserId = person,
        Distance = 1,
        LeadershipPriority = 1,
    };
}

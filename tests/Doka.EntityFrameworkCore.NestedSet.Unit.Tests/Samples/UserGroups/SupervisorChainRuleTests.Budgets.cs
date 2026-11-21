namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

public sealed partial class SupervisorChainRuleTests
{
    /// <summary>Every chain needs explicit positive reader, retained-state, and frontier bounds.</summary>
    /// <param name="dimension">The chain budget dimension whose bound is invalid.</param>
    /// <param name="limit">The nonpositive bound rejected before chain execution.</param>
    [Theory]
    [InlineData("frontier", 0)]
    [InlineData("frontier", -1)]
    [InlineData("visited", 0)]
    [InlineData("visited", -1)]
    [InlineData("rows", 0)]
    [InlineData("rows", -1)]
    public void Validate_NonpositiveChainBudget_IsRejected(
        string dimension,
        int limit
    )
    {
        // Arrange
        var request = dimension switch
        {
            "frontier" => Request() with { MaxFrontierStates = limit },
            "visited" => Request() with { MaxVisitedStates = limit },
            _ => Request() with { MaxResultRows = limit },
        };

        // Act
        var error = Record.Exception(() => request.Validate(originalPath: true));

        // Assert
        Assert.IsType<ArgumentException>(error);
    }

    /// <summary>The largest supported row budget retains one representable SQL overflow-probe row.</summary>
    /// <param name="originalPath">Whether the request follows its original membership path.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Validate_MaximumRepresentableOverflowProbe_IsAccepted(
        bool originalPath
    )
    {
        // Arrange
        var request = Request() with { MaxResultRows = int.MaxValue - 1 };

        // Act
        var error = Record.Exception(() => request.Validate(originalPath));

        // Assert
        Assert.Null(error);
    }

    /// <summary>A row budget without room for one SQL overflow-probe row is rejected before execution.</summary>
    /// <param name="originalPath">Whether the request follows its original membership path.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Validate_UnrepresentableOverflowProbe_IsRejected(
        bool originalPath
    )
    {
        // Arrange
        var request = Request() with { MaxResultRows = int.MaxValue };

        // Act
        var error = Record.Exception(() => request.Validate(originalPath));

        // Assert
        Assert.IsType<ArgumentException>(error);
    }

    /// <summary>
    /// Logical fan-out stops before appending a second path when any explicit growth budget is exhausted.
    /// </summary>
    /// <param name="dimension">The logical growth budget that permits only the first candidate path.</param>
    [Theory]
    [InlineData("frontier")]
    [InlineData("visited")]
    [InlineData("rows")]
    public void AdvanceBranch_FanOut_StopsBeforeAppendingBeyondBudget(
        string dimension
    )
    {
        // Arrange
        var request = dimension switch
        {
            "frontier" => Request() with { MaxFrontierStates = 1 },
            "visited" => Request() with { MaxVisitedStates = 1 },
            _ => Request() with { MaxResultRows = 1 },
        };

        var first = Candidate(21, 7, 6);
        var second = Candidate(22, 7, 5);
        var resolution = new SupervisorResolution(SupervisorResolutionStatus.Found, 7, [first, second], null);
        List<SupervisorQueries.ChainPath> next = [];
        List<SupervisorChainBranch> completed = [];
        var budget = new SupervisorQueries.ChainBudget(request);

        // Act
        var admitted = SupervisorQueries.AdvanceBranch(
            resolution,
            null,
            42,
            42,
            7,
            Guid.Empty,
            1,
            SupervisorSelection.All,
            budget,
            next,
            completed);

        // Assert
        Assert.False(admitted);
        Assert.Empty(completed);
        var retained = Assert.Single(next);
        Assert.Same(first, retained.Hop.Evidence);
        Assert.Null(retained.Previous);
    }

    /// <summary>Terminal outcomes budget every copied successful hop as well as the terminal status itself.</summary>
    /// <param name="outcome">The missing or cyclic terminal outcome following the shared prefix.</param>
    /// <param name="maximumRows">The logical result-row budget available for the terminal copy.</param>
    /// <param name="expected">Whether the full prefix and terminal status fit within the budget.</param>
    [Theory]
    [InlineData("missing", 2, false)]
    [InlineData("missing", 3, true)]
    [InlineData("cycle", 2, false)]
    [InlineData("cycle", 3, true)]
    public void AdvanceBranch_TerminalPath_CopyRequiresTheFullLogicalRowBudget(
        string outcome,
        int maximumRows,
        bool expected
    )
    {
        // Arrange
        var previous = Prefix();
        var resolution = outcome == "cycle"
            ? Found(Candidate(42, 2, 1))
            : new SupervisorResolution(SupervisorResolutionStatus.NotFound, 5, [], null);

        var budget = new SupervisorQueries.ChainBudget(Request() with { MaxResultRows = maximumRows });
        List<SupervisorQueries.ChainPath> next = [];
        List<SupervisorChainBranch> completed = [];
        int[] expectedPeople = expected
            ? [21, 22]
            : [];

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
        Assert.Equal(expected, admitted);
        Assert.Empty(next);
        Assert.Equal(
            expected
                ? 1
                : 0,
            completed.Count);

        Assert.Equal(
            expectedPeople,
            completed
                .SelectMany(branch => branch.Hops)
                .Select(hop => hop.Evidence.CandidateUserId));
    }

    /// <summary>A large requested copy cannot overflow a counter or bypass a small remaining budget.</summary>
    [Fact]
    public void Result_ProspectiveResult_CopyBeyondBudgetIsRejectedWithoutOverflow()
    {
        // Arrange
        var budget = new SupervisorQueries.ChainBudget(Request() with { MaxResultRows = 2 });

        // Act
        var admitted = budget.Result(int.MaxValue);

        // Assert
        Assert.False(admitted);
    }

    /// <summary>A rejected prospective copy does not consume the last permitted result row.</summary>
    [Fact]
    public void Result_RejectedResult_CopyDoesNotAdvanceTheCounter()
    {
        // Arrange
        var budget = new SupervisorQueries.ChainBudget(Request() with { MaxResultRows = 1 });
        var oversized = budget.Result(int.MaxValue);

        // Act
        var admitted = budget.Result();

        // Assert
        Assert.False(oversized);
        Assert.True(admitted);
    }

    /// <summary>Physical reads reject the first row beyond the capacity before it can enter a buffer.</summary>
    [Fact]
    public void Read_ReaderBudget_RejectsCapacityPlusOne()
    {
        // Arrange
        var budget = new SupervisorQueries.ChainBudget(Request() with { MaxResultRows = 2 });
        budget.Read();
        budget.Read();

        // Act
        var admitted = budget.Read();

        // Assert
        Assert.False(admitted);
    }

    /// <summary>
    /// Physical reads accept the last allowed row rather than truncating before the declared capacity.
    /// </summary>
    /// <param name="capacity">The exact number of physical rows allowed by the reader budget.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Read_ReaderBudget_AcceptsExactCapacity(
        int capacity
    )
    {
        // Arrange
        var budget = new SupervisorQueries.ChainBudget(Request() with { MaxResultRows = capacity });

        // WHY: Consume only the preceding rows so the act checks admission at the exact capacity boundary.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 1; index < capacity; index++)
        {
            budget.Read();
        }

        // Act
        var admitted = budget.Read();

        // Assert
        Assert.True(admitted);
    }

    /// <summary>Physical reader usage does not consume the separate allowance for retained logical paths.</summary>
    [Fact]
    public void Result_Physical_ReadsAndLogicalResultsHaveIndependentCapacity()
    {
        // Arrange
        var budget = new SupervisorQueries.ChainBudget(Request() with { MaxResultRows = 1 });
        var physicalRead = budget.Read();

        // Act
        var admitted = budget.Result();

        // Assert
        Assert.True(physicalRead);
        Assert.True(admitted);
    }
}

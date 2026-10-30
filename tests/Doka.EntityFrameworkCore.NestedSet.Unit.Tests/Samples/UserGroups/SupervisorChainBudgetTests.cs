namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

/// <summary>Checks independent reader and visited-state counters before any retained chain allocation.</summary>
// WHY: Strict per-thread allocation measurements must run outside the Unit suite's concurrent
// runtime diagnostics and initialization. The admission and exhaustion assertions remain unchanged.
[Collection("Allocation measurements")]
public sealed class SupervisorChainBudgetTests
{
    /// <summary>Each counter accepts its capacity, then rejects without consuming another budget.</summary>
    /// <param name="counter">The physical-reader or retained-visit allowance being exhausted.</param>
    [Theory]
    [InlineData("reads")]
    [InlineData("visits")]
    public void ExhaustedCounterPreservesOtherAllowances(string counter)
    {
        // Arrange
        var request = Request();
        var budget = new SupervisorQueries.ChainBudget(request);

        // Act
        var first = counter == "reads" ? budget.Read() : budget.Visit();
        var second = counter == "reads" ? budget.Read() : budget.Visit();
        var rejected = counter == "reads" ? budget.Read() : budget.Visit();
        var rejectedAgain = counter == "reads" ? budget.Read() : budget.Visit();
        var other = counter == "reads" ? budget.Visit() : budget.Read();
        var result = budget.Result();

        // Assert
        Assert.True(first);
        Assert.True(second);
        Assert.False(rejected);
        Assert.False(rejectedAgain);
        Assert.True(other);
        Assert.True(result);
        Assert.Equal(counter == "reads" ? 0 : 1, budget.RemainingReads);
    }

    /// <summary>Counter admission does not allocate while checking and consuming existing request limits.</summary>
    [Fact]
    public void CounterAdmissionAndExhaustionDoNotAllocate()
    {
        // Arrange
        const int operations = 10_000;
        var budget = new SupervisorQueries.ChainBudget(Request() with
        {
            MaxResultRows = operations + 1,
            MaxVisitedStates = operations + 1,
        });

        // WHY: Warm both methods before measuring; the contract concerns per-admission managed allocations.
        budget.Read();
        budget.Visit();
        var admitted = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();

        // Act
        for (var index = 0; index < operations; index++)
        {
            if (budget.Read() && budget.Visit())
            {
                admitted++;
            }
        }

        var rejectedRead = budget.Read();
        var rejectedVisit = budget.Visit();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        Assert.Equal(operations, admitted);
        Assert.False(rejectedRead);
        Assert.False(rejectedVisit);
        Assert.Equal(0, budget.RemainingReads);
        Assert.Equal(0, allocated);
    }

    /// <summary>Creates explicit independent limits without catalog or database dependencies.</summary>
    private static SupervisorChainRequest Request() => new()
    {
        SourceUserId = 42,
        Steps =
        [
            new(SearchStart.ParentsOnly, new NoRoleFilter(), new AllEligibleGroups(), [], null,
                SupervisorSelection.RequireUnique),
        ],
        RoutingOrigin = RoutingOrigin.InitialSubject,
        MaxFrontierStates = 2,
        MaxVisitedStates = 2,
        MaxResultRows = 2,
    };
}

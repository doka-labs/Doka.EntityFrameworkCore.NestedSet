namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

/// <summary>Guards retained frontier allocations without charging an extra transport array per unique cursor.</summary>
/// <param name="output">Receives observed allocation totals outside the measured operation.</param>
[Trait("Category", "Allocation")]
public sealed class ChainCostAllocationTests(ITestOutputHelper output)
{
    /// <summary>A canonical unique occurrence creates only its retained hop and immutable path node.</summary>
    [Fact]
    public void UniqueContinuationsAvoidAnAdditionalArrayPerLogicalPath()
    {
        // Arrange
        const int paths = 1_024;
        var request = new SupervisorChainRequest
        {
            SourceUserId = 42, Steps = [], RoutingOrigin = RoutingOrigin.InitialSubject,
            MaxFrontierStates = paths, MaxVisitedStates = paths, MaxResultRows = paths,
        };

        var candidate = new SupervisorCandidate
        {
            SourceUserId = 42, StartGroupId = 7, FoundGroupId = 6, CandidateUserId = 21, Distance = 1,
        };

        var resolution = new SupervisorResolution(SupervisorResolutionStatus.Found, 7, [candidate], candidate);
        var warm = new SupervisorQueries.ChainBudget(request);
        var warmPaths = new List<SupervisorQueries.ChainPath>(4);
        var completed = new List<SupervisorChainBranch>();
        for (var iteration = 0; iteration < 4; iteration++)
        {
            _ = SupervisorQueries.AdvanceBranch(resolution, null, 42, 42, 7, Guid.Empty, 1,
                SupervisorSelection.RequireUnique, warm, warmPaths, completed);
        }

        var budget = new SupervisorQueries.ChainBudget(request);
        var next = new List<SupervisorQueries.ChainPath>(paths);
        var admitted = true;
        var before = GC.GetAllocatedBytesForCurrentThread();

        // Act
        for (var index = 0; index < paths; index++)
        {
            admitted &= SupervisorQueries.AdvanceBranch(resolution, null, 42, 42, 7, Guid.Empty, 1,
                SupervisorSelection.RequireUnique, budget, next, completed);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        Assert.True(admitted);
        Assert.Equal(paths, next.Count);
        Assert.Empty(completed);
        Assert.All(next, path => Assert.Same(candidate, path.Hop.Evidence));
        // WHY: 128 bytes per state admit the actual hop and prefix node with layout headroom. Adding a
        // separate one-element occurrence array for every state exceeds that linear retained-object budget.
        Assert.InRange(allocated, 0, paths * 128L + 4_096);
        output.WriteLine($"Paths={paths}; continuation allocation={allocated} B.");
        GC.KeepAlive(next);
    }
}

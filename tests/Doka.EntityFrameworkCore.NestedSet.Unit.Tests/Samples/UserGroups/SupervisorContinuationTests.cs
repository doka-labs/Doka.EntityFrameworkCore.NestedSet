namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

/// <summary>Checks retained chain continuations and exact evidence identity.</summary>
public sealed class SupervisorContinuationTests
{
    /// <summary>Each canonical unique occurrence retains one frontier path with the original hop evidence.</summary>
    [Fact]
    public void AdvanceBranch_UniqueContinuations_RetainsEachFrontierPath()
    {
        // Arrange
        const int paths = 1_024;
        var request = new SupervisorChainRequest
        {
            SourceUserId = 42,
            Steps = [],
            RoutingOrigin = RoutingOrigin.InitialSubject,
            MaxFrontierStates = paths,
            MaxVisitedStates = paths,
            MaxResultRows = paths,
        };

        var candidate = new SupervisorCandidate
        {
            SourceUserId = 42,
            StartGroupId = 7,
            FoundGroupId = 6,
            CandidateUserId = 21,
            Distance = 1,
        };

        var resolution = new SupervisorResolution(SupervisorResolutionStatus.Found, 7, [candidate], candidate);
        List<SupervisorChainBranch> completed = [];

        var budget = new SupervisorQueries.ChainBudget(request);
        var next = new List<SupervisorQueries.ChainPath>(paths);
        var admitted = true;

        // Act
        // WHY: Every admission contributes one frontier path; retain its independently checked count and evidence.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < paths; index++)
        {
            admitted &= SupervisorQueries.AdvanceBranch(
                resolution,
                null,
                42,
                42,
                7,
                Guid.Empty,
                1,
                SupervisorSelection.RequireUnique,
                budget,
                next,
                completed);
        }


        // Assert
        Assert.True(admitted);
        Assert.Equal(paths, next.Count);
        Assert.Empty(completed);
        Assert.All(next, path => Assert.Same(candidate, path.Hop.Evidence));
    }
}

using System.Data;
using Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

public sealed partial class UserGroupChainBenchmarkTests
{
    /// <summary>Convergent paths retain shared managers as independent valid provenance.</summary>
    /// <param name="profile">The explicit branching workload.</param>
    /// <param name="reference">Whether the independent path arrays are executed.</param>
    [Theory]
    [InlineData(BenchmarkChainProfile.Hops4Fanout2, false)]
    [InlineData(BenchmarkChainProfile.Hops4Fanout2, true)]
    [InlineData(BenchmarkChainProfile.Hops2Fanout4, false)]
    [InlineData(BenchmarkChainProfile.Hops2Fanout4, true)]
    public async Task ExecuteAsync_ConvergentFanout_PreservesIndependentCompletePaths(
        BenchmarkChainProfile profile,
        bool reference
    )
    {
        // Arrange
        await using var benchmark = Create(profile, reexpand: true);
        benchmark.Initialize();
        await benchmark.PrepareAsync(TestContext.Current.CancellationToken);
        var (hops, fanout) = UserGroupChainBenchmarkData.Dimensions(profile);
        var expected = (int)Math.Pow(fanout, hops);

        // Act
        await ExecuteAsync(benchmark, reference, TestContext.Current.CancellationToken);

        // Assert
        await benchmark.VerifyAsync(TestContext.Current.CancellationToken);
        Assert.True(benchmark.Result.Complete);
        Assert.Equal(expected, benchmark.Result.Branches.Count);
        Assert.All(
            benchmark.Result.Branches,
            branch =>
                Assert.Equal(SupervisorChainStatus.ReachedRequestedHop, branch.Status));

        Assert.Equal(
            fanout,
            benchmark
                .Result
                .Branches
                .Select(branch => branch.Hops[^1].Evidence.CandidateUserId)
                .Distinct()
                .Count());
    }

    /// <summary>Ambiguity remains explicit even when the physical budget cannot buffer all candidates.</summary>
    [Fact]
    public async Task RunApiAsync_SmallReaderBudget_DoesNotTurnAmbiguityIntoOverflow()
    {
        // Arrange
        await using var benchmark = Create(BenchmarkChainProfile.Ambiguous);
        benchmark.Initialize();
        await benchmark.PrepareAsync(TestContext.Current.CancellationToken);
        var request = benchmark.Request with { MaxResultRows = 2 };

        // Act
        var actual = await benchmark.RunApiAsync(request, CancellationToken.None);
        var reference = await UserGroupChainReference.FollowAsync(
            benchmark.Context,
            benchmark.Data.Tenant,
            request,
            true,
            CancellationToken.None);

        // Assert
        Assert.Equal(UserGroupChainBenchmarks.Signature(actual), UserGroupChainBenchmarks.Signature(reference));
        Assert.True(actual.Complete);
        Assert.Equal(
            SupervisorChainStatus.Ambiguous,
            Assert.Single(actual.Branches)
                .Status);
    }

    /// <summary>Explicit business priority and occupied person slots keep the same canonical continuation.</summary>
    /// <param name="secondPerson">Whether the second person instead of the best priority is requested.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunApiAsync_BusinessPersonSelection_RetainsItsCanonicalCursor(
        bool secondPerson
    )
    {
        // Arrange
        await using var benchmark = Create(BenchmarkChainProfile.Hops4Fanout2);
        benchmark.Initialize();
        await benchmark.PrepareAsync(TestContext.Current.CancellationToken);
        var preferred = Enumerable
            .Range(0, 8)
            .Select(level => benchmark.Data.People[1 + level * 2])
            .ToArray();

        await benchmark
            .Context
            .UserGroupMemberships
            .Where(member => Enumerable.Contains(preferred, member.UserId))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(member => member.LeadershipPriority, 5),
                CancellationToken.None);

        var selection = secondPerson
            ? SupervisorSelection.NthPerson(2)
            : SupervisorSelection.HighestBusinessPriority;

        var request = benchmark.Request with
        {
            Steps = benchmark
                .Request
                .Steps
                .Select(step => step with { Selection = selection })
                .ToArray(),
        };

        // Act
        var actual = await benchmark.RunApiAsync(request, CancellationToken.None);
        var reference = await UserGroupChainReference.FollowAsync(
            benchmark.Context,
            benchmark.Data.Tenant,
            request,
            true,
            CancellationToken.None);

        // Assert
        Assert.Equal(UserGroupChainBenchmarks.Signature(actual), UserGroupChainBenchmarks.Signature(reference));
        var branch = Assert.Single(actual.Branches);
        Assert.Equal(SupervisorChainStatus.ReachedRequestedHop, branch.Status);
        Assert.All(
            branch.Hops,
            hop => Assert.Equal(
                secondPerson
                    ? 10
                    : 5,
                hop.Evidence.LeadershipPriority));
    }

    /// <summary>A requested person slot inside a tie is ambiguous; a slot beyond all people is missing.</summary>
    /// <param name="number">The explicitly requested one-based person slot.</param>
    /// <param name="expected">The resulting explicit chain status.</param>
    [Theory]
    [InlineData(2, "Ambiguous")]
    [InlineData(3, "NoSupervisor")]
    public async Task RunApiAsync_PersonRank_DoesNotInventWinnerOrFallback(
        int number,
        string expected
    )
    {
        // Arrange
        await using var benchmark = Create(BenchmarkChainProfile.Hops4Fanout2);
        benchmark.Initialize();
        await benchmark.PrepareAsync(TestContext.Current.CancellationToken);
        var request = benchmark.Request with
        {
            Steps = benchmark
                .Request
                .Steps
                .Select(step => step with { Selection = SupervisorSelection.NthPerson(number) })
                .ToArray(),
        };

        // Act
        var actual = await benchmark.RunApiAsync(request, CancellationToken.None);
        var reference = await UserGroupChainReference.FollowAsync(
            benchmark.Context,
            benchmark.Data.Tenant,
            request,
            true,
            CancellationToken.None);

        // Assert
        Assert.Equal(UserGroupChainBenchmarks.Signature(actual), UserGroupChainBenchmarks.Signature(reference));
        Assert.Equal(
            expected,
            Assert
                .Single(actual.Branches)
                .Status
                .ToString());

        Assert.True(actual.Complete);
    }
}

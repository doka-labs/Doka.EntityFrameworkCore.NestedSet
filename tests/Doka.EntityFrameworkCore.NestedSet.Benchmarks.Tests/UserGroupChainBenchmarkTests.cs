using System.Data;
using Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>Checks complete actual-chain/reference equivalence before observational benchmark timing.</summary>
[Collection(BenchmarkEnvironmentOwnership.Name)]
public sealed class UserGroupChainBenchmarkTests
{
    /// <summary>Native code generation and the existing launcher classify the actual sample chain correctly.</summary>
    [Fact]
    public void NativeValidationAcceptsActualChainLifecycle()
    {
        // Arrange
        var configuration = ManualConfig
            .Create(BenchmarkConfiguration.Create(new BenchmarkRunOptions()))
            .AddFilter(new GlobFilter(["*UserGroupChainBenchmarks.*"]));

        using var discovery = BenchmarkSelection.Discover(configuration, diagnostics: true);
        using var native = BenchmarkConverter.TypeToBenchmarks(typeof(UserGroupChainBenchmarks), configuration);
        var validation = new ValidationParameters(native.BenchmarksCases, native.Config);

        // Act
        var errors = native
            .Config
            .GetValidators()
            .SelectMany(validator => validator.Validate(validation))
            .Where(error => error.IsCritical)
            .ToArray();

        // Assert
        Assert.True(discovery.RequiresDatabase);
        Assert.NotEmpty(discovery.DatabaseCases);
        Assert.Empty(errors);
        Assert.All(discovery.DatabaseCases,
            item => Assert.Equal(typeof(UserGroupChainBenchmarks), item.Descriptor.Type));
        Assert.False(typeof(DatabaseBenchmark).IsAssignableFrom(typeof(UserGroupChainBenchmarks)));
    }

    /// <summary>Every requested successful hop uses a real persisted level and its exact continuation cursor.</summary>
    /// <param name="profile">The explicit two-, four-, or eight-hop profile.</param>
    /// <param name="reexpand">Whether subsequent steps re-evaluate all own memberships.</param>
    /// <param name="reference">Whether the independent per-path orchestration is measured.</param>
    [Theory]
    [InlineData(BenchmarkChainProfile.Hops2Fanout1, false, false)]
    [InlineData(BenchmarkChainProfile.Hops2Fanout1, true, true)]
    [InlineData(BenchmarkChainProfile.Hops4Fanout1, false, true)]
    [InlineData(BenchmarkChainProfile.Hops4Fanout1, true, false)]
    [InlineData(BenchmarkChainProfile.Hops8Fanout1, false, false)]
    [InlineData(BenchmarkChainProfile.Hops8Fanout1, true, true)]
    public async Task ActualHopCountAndCursorMatchIndependentPath(
        BenchmarkChainProfile profile,
        bool reexpand,
        bool reference
    )
    {
        // Arrange
        await using var benchmark = Create(profile, reexpand);
        benchmark.Initialize();
        await benchmark.PrepareAsync();
        var hops = UserGroupChainBenchmarkData.Dimensions(profile).Hops;

        // Act
        await ExecuteAsync(benchmark, reference);

        // Assert
        await benchmark.VerifyAsync();
        var branch = Assert.Single(benchmark.Result.Branches);
        Assert.Equal(SupervisorChainStatus.ReachedRequestedHop, branch.Status);
        Assert.Equal(hops, branch.Hops.Count);
        Assert.Equal(benchmark.Data.Groups[8 - hops], branch.Hops[branch.Hops.Count - 1].Evidence.FoundGroupId);
        Assert.All(branch.Hops, hop =>
        {
            Assert.Equal(benchmark.Data.Groups[8], hop.InitialGroupId);
            Assert.Equal(benchmark.Data.Groups[9 - hop.Number], hop.Evidence.StartGroupId);
            Assert.Equal(benchmark.Data.Groups[8 - hop.Number], hop.Evidence.FoundGroupId);
            Assert.Equal(1, hop.Evidence.Distance);
        });
    }

    /// <summary>Convergent paths retain shared managers as independent valid provenance.</summary>
    /// <param name="profile">The explicit branching workload.</param>
    /// <param name="reference">Whether the independent path arrays are executed.</param>
    [Theory]
    [InlineData(BenchmarkChainProfile.Hops4Fanout2, false)]
    [InlineData(BenchmarkChainProfile.Hops4Fanout2, true)]
    [InlineData(BenchmarkChainProfile.Hops2Fanout4, false)]
    [InlineData(BenchmarkChainProfile.Hops2Fanout4, true)]
    public async Task ConvergentFanoutPreservesIndependentCompletePaths(
        BenchmarkChainProfile profile,
        bool reference
    )
    {
        // Arrange
        await using var benchmark = Create(profile, reexpand: true);
        benchmark.Initialize();
        await benchmark.PrepareAsync();
        var (hops, fanout) = UserGroupChainBenchmarkData.Dimensions(profile);
        var expected = (int)Math.Pow(fanout, hops);

        // Act
        await ExecuteAsync(benchmark, reference);

        // Assert
        await benchmark.VerifyAsync();
        Assert.True(benchmark.Result.Complete);
        Assert.Equal(expected, benchmark.Result.Branches.Count);
        Assert.All(benchmark.Result.Branches, branch =>
            Assert.Equal(SupervisorChainStatus.ReachedRequestedHop, branch.Status));
        Assert.Equal(fanout,
            benchmark.Result.Branches.Select(branch => branch.Hops[^1].Evidence.CandidateUserId)
                .Distinct().Count());
    }

    /// <summary>Missing managers, ambiguity, cycles, and frontier overflow retain equal explicit outcomes.</summary>
    /// <param name="profile">The adversarial actual-model dataset.</param>
    /// <param name="expected">The explicit branch termination.</param>
    /// <param name="reference">Whether the independent baseline is executed.</param>
    [Theory]
    [InlineData(BenchmarkChainProfile.Missing, "NoSupervisor", false)]
    [InlineData(BenchmarkChainProfile.Missing, "NoSupervisor", true)]
    [InlineData(BenchmarkChainProfile.Ambiguous, "Ambiguous", false)]
    [InlineData(BenchmarkChainProfile.Ambiguous, "Ambiguous", true)]
    [InlineData(BenchmarkChainProfile.Cycle, "CycleDetected", false)]
    [InlineData(BenchmarkChainProfile.Cycle, "CycleDetected", true)]
    [InlineData(BenchmarkChainProfile.FrontierLimit, "LimitExceeded", false)]
    [InlineData(BenchmarkChainProfile.FrontierLimit, "LimitExceeded", true)]
    public async Task NegativeProfilesRetainStatusWithoutInventingHops(
        BenchmarkChainProfile profile,
        string expected,
        bool reference
    )
    {
        // Arrange
        await using var benchmark = Create(profile);
        benchmark.Initialize();
        await benchmark.PrepareAsync();

        // Act
        await ExecuteAsync(benchmark, reference);

        // Assert
        await benchmark.VerifyAsync();
        var branch = Assert.Single(benchmark.Result.Branches);
        Assert.Equal(expected, branch.Status.ToString());
        Assert.Equal(expected != "LimitExceeded", benchmark.Result.Complete);
        Assert.DoesNotContain(branch.Hops, hop => hop.Evidence.CandidateUserId == benchmark.Data.Request.SourceUserId);
    }

    /// <summary>Initial and current routing bind different real manager occurrences after the first hop.</summary>
    /// <param name="current">Whether routing follows the current manager's different key.</param>
    /// <param name="reference">Whether the per-path baseline is executed.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RoutingPolicyRetainsItsExactSubjectAcrossHops(
        bool current,
        bool reference
    )
    {
        // Arrange
        await using var benchmark = Create(BenchmarkChainProfile.RoutingSwitch, currentRouting: current);
        benchmark.Initialize();
        await benchmark.PrepareAsync();

        // Act
        await ExecuteAsync(benchmark, reference);

        // Assert
        await benchmark.VerifyAsync();
        var branch = Assert.Single(benchmark.Result.Branches);
        Assert.Equal(SupervisorChainStatus.ReachedRequestedHop, branch.Status);
        Assert.Equal(benchmark.Data.People[1 + (4 * 2) + (current ? 1 : 0)],
            branch.Hops[^1].Evidence.CandidateUserId);
    }

    /// <summary>Only explicit re-expansion starts a later hop from the manager's additional own membership.</summary>
    /// <param name="reexpand">Whether all memberships are re-evaluated.</param>
    /// <param name="reference">Whether the independent baseline is executed.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ReExpansionDoesNotSilentlyReplaceOriginalPath(
        bool reexpand,
        bool reference
    )
    {
        // Arrange
        await using var benchmark = Create(BenchmarkChainProfile.ReExpansion, reexpand);
        benchmark.Initialize();
        await benchmark.PrepareAsync();

        // Act
        await ExecuteAsync(benchmark, reference);

        // Assert
        await benchmark.VerifyAsync();
        Assert.Equal(reexpand, benchmark.Result.Branches.SelectMany(branch => branch.Hops)
            .Any(hop => hop.Number > 1 && hop.Evidence.StartGroupId == benchmark.Data.Groups[9]));
    }

    /// <summary>Ambiguity remains explicit even when the physical budget cannot buffer all candidates.</summary>
    [Fact]
    public async Task SmallReaderBudgetDoesNotTurnAmbiguityIntoOverflow()
    {
        // Arrange
        await using var benchmark = Create(BenchmarkChainProfile.Ambiguous);
        benchmark.Initialize();
        await benchmark.PrepareAsync();
        var request = benchmark.Request with { MaxResultRows = 2 };

        // Act
        var actual = await benchmark.RunApiAsync(request, CancellationToken.None);
        var reference = await UserGroupChainReference.FollowAsync(benchmark.Context, benchmark.Data.Tenant,
            request, true, CancellationToken.None);

        // Assert
        Assert.Equal(UserGroupChainBenchmarks.Signature(actual), UserGroupChainBenchmarks.Signature(reference));
        Assert.True(actual.Complete);
        Assert.Equal(SupervisorChainStatus.Ambiguous, Assert.Single(actual.Branches).Status);
    }

    /// <summary>Explicit business priority and occupied person slots keep the same canonical continuation.</summary>
    /// <param name="secondPerson">Whether the second person instead of the best priority is requested.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BusinessPersonSelectionRetainsItsCanonicalCursor(
        bool secondPerson
    )
    {
        // Arrange
        await using var benchmark = Create(BenchmarkChainProfile.Hops4Fanout2);
        benchmark.Initialize();
        await benchmark.PrepareAsync();
        var preferred = Enumerable
            .Range(0, 8)
            .Select(level => benchmark.Data.People[1 + (level * 2)])
            .ToArray();

        await benchmark
            .Context
            .UserGroupMemberships
            .Where(member => Enumerable.Contains(preferred, member.UserId))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(member => member.LeadershipPriority, 5),
                CancellationToken.None);

        var selection = secondPerson ? SupervisorSelection.NthPerson(2) : SupervisorSelection.HighestBusinessPriority;
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
        Assert.All(branch.Hops, hop => Assert.Equal(secondPerson ? 10 : 5, hop.Evidence.LeadershipPriority));
    }

    /// <summary>A requested person slot inside a tie is ambiguous; a slot beyond all people is missing.</summary>
    /// <param name="number">The explicitly requested one-based person slot.</param>
    /// <param name="expected">The resulting explicit chain status.</param>
    [Theory]
    [InlineData(2, "Ambiguous")]
    [InlineData(3, "NoSupervisor")]
    public async Task PersonRankDoesNotInventWinnerOrFallback(
        int number,
        string expected
    )
    {
        // Arrange
        await using var benchmark = Create(BenchmarkChainProfile.Hops4Fanout2);
        benchmark.Initialize();
        await benchmark.PrepareAsync();
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
        Assert.Equal(expected, Assert.Single(actual.Branches).Status.ToString());
        Assert.True(actual.Complete);
    }

    /// <summary>The overflow probe rejects a row before buffering a misleadingly complete partial result.</summary>
    /// <param name="visited">Whether the visited limit instead of the physical reader limit is exhausted.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FiniteReaderAndVisitedLimitsRemainIncomplete(
        bool visited
    )
    {
        // Arrange
        await using var benchmark = Create(BenchmarkChainProfile.Hops4Fanout1);
        benchmark.Initialize();
        await benchmark.PrepareAsync();
        var request = visited
            ? benchmark.Request with { MaxVisitedStates = 1 }
            : benchmark.Request with { MaxResultRows = 1 };

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
        Assert.False(actual.Complete);
        Assert.Contains(actual.Branches, branch => branch.Status == SupervisorChainStatus.LimitExceeded);
    }

    /// <summary>Absent source and absent selected own membership remain distinct request-level outcomes.</summary>
    /// <param name="missingUser">Whether the visible user itself is absent.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingRequestOriginsAreNotSuccessfulEmptyChains(
        bool missingUser
    )
    {
        // Arrange
        await using var benchmark = Create(BenchmarkChainProfile.Hops2Fanout1);
        benchmark.Initialize();
        await benchmark.PrepareAsync();
        var request = missingUser
            ? benchmark.Request with { SourceUserId = int.MaxValue }
            : benchmark.Request with { StartGroupId = benchmark.Data.Groups[0] };

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
        Assert.Equal(missingUser
                ? SupervisorBatchStatus.SourceNotFound
                : SupervisorBatchStatus.NoVisibleMemberships,
            actual.Status);
        Assert.Empty(actual.Branches);
    }

    /// <summary>An unknown role remains an invalid request without silently disabling the role filter.</summary>
    [Fact]
    public async Task UnknownRoleIsNotConvertedIntoAnUnfilteredOrMissingChain()
    {
        // Arrange
        await using var benchmark = Create(BenchmarkChainProfile.Hops2Fanout1);
        benchmark.Initialize();
        await benchmark.PrepareAsync();
        var request = benchmark.Request with
        {
            Steps = benchmark
                .Request
                .Steps
                .Select(step => step with { Roles = new MembershipRoleFilter(Guid.NewGuid()) })
                .ToArray(),
        };

        // Act
        var actual = await Record.ExceptionAsync(() => benchmark.RunApiAsync(request, CancellationToken.None));
        var reference = await Record.ExceptionAsync(() => UserGroupChainReference.FollowAsync(
            benchmark.Context,
            benchmark.Data.Tenant,
            request,
            true,
            CancellationToken.None));

        // Assert
        Assert.IsType<ArgumentException>(actual);
        Assert.IsType<ArgumentException>(reference);
    }

    /// <summary>Both complete chains use an adequate caller snapshot without replacing or committing it.</summary>
    [Fact]
    public async Task AdequateCallerSnapshotRemainsOwnedByTheCaller()
    {
        // Arrange
        await using var benchmark = Create(BenchmarkChainProfile.Hops2Fanout1);
        benchmark.Initialize();
        await benchmark.PrepareAsync();
        await using var transaction = await benchmark.Context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            CancellationToken.None);

        // Act
        var actual = await benchmark.RunApiAsync(benchmark.Request, CancellationToken.None);
        var reference = await UserGroupChainReference.FollowAsync(
            benchmark.Context,
            benchmark.Data.Tenant,
            benchmark.Request,
            true,
            CancellationToken.None);

        // Assert
        Assert.Equal(UserGroupChainBenchmarks.Signature(actual), UserGroupChainBenchmarks.Signature(reference));
        Assert.Same(transaction, benchmark.Context.Database.CurrentTransaction);
    }

    /// <summary>A precanceled token is not translated into a missing or successful partial chain.</summary>
    [Fact]
    public async Task CancellationIsNotAnEmptyBusinessResult()
    {
        // Arrange
        await using var benchmark = Create(BenchmarkChainProfile.Hops2Fanout1);
        benchmark.Initialize();
        await benchmark.PrepareAsync();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var actual = await Record.ExceptionAsync(() => benchmark.RunApiAsync(benchmark.Request, cancellation.Token));
        var reference = await Record.ExceptionAsync(() => UserGroupChainReference.FollowAsync(
            benchmark.Context,
            benchmark.Data.Tenant,
            benchmark.Request,
            true,
            cancellation.Token));

        // Assert
        Assert.IsType<OperationCanceledException>(actual, exactMatch: false);
        Assert.IsType<OperationCanceledException>(reference, exactMatch: false);
    }

    /// <summary>Verification cannot treat a never-executed timed operation as a successful benchmark.</summary>
    [Fact]
    public async Task VerificationRejectsAnUnexecutedChain()
    {
        // Arrange
        await using var benchmark = Create(BenchmarkChainProfile.Hops2Fanout1);
        benchmark.Initialize();
        await benchmark.PrepareAsync();

        // Act
        var error = await Record.ExceptionAsync(benchmark.VerifyAsync);

        // Assert
        Assert.IsType<InvalidOperationException>(error);
    }

    /// <summary>Creates one small actual-model benchmark without changing global runner configuration.</summary>
    private static UserGroupChainBenchmarks Create(
        BenchmarkChainProfile profile,
        bool reexpand = false,
        bool currentRouting = false
    ) => new()
    {
        Nodes = 24,
        Shape = BenchmarkShape.Balanced,
        Trees = 1,
        Tracked = 0,
        Profile = profile,
        ReExpandMemberships = reexpand,
        CurrentSubjectRouting = currentRouting,
    };

    /// <summary>Selects exactly one timed operation for each independently arranged test case.</summary>
    private static Task ExecuteAsync(
        UserGroupChainBenchmarks benchmark,
        bool reference
    ) => reference ? benchmark.Reference() : benchmark.Api();
}

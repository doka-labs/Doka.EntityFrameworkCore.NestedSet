using Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Names actual finite chain, fan-out, routing, and rejection datasets.</summary>
public enum BenchmarkChainProfile
{
    /// <summary>Requests two successful hops with one candidate per level.</summary>
    Hops2Fanout1,

    /// <summary>Requests four successful hops with one candidate per level.</summary>
    Hops4Fanout1,

    /// <summary>Requests eight successful hops with one candidate per level.</summary>
    Hops8Fanout1,

    /// <summary>Requests two hops with two candidate paths at each level.</summary>
    Hops2Fanout2,

    /// <summary>Requests four hops with two candidate paths at each level.</summary>
    Hops4Fanout2,

    /// <summary>Requests eight hops with two candidate paths at each level.</summary>
    Hops8Fanout2,

    /// <summary>Requests two hops with four candidate paths at each level.</summary>
    Hops2Fanout4,

    /// <summary>Requests four hops with four candidate paths at each level.</summary>
    Hops4Fanout4,

    /// <summary>Requests eight hops with four candidates; finite frontier overflow remains explicit.</summary>
    Hops8Fanout4,

    /// <summary>Stops on the second exact type group because it has no permitted manager.</summary>
    Missing,

    /// <summary>Rejects two distinct managers rather than choosing a technical identity.</summary>
    Ambiguous,

    /// <summary>Detects the initial person again on a strictly higher group.</summary>
    Cycle,

    /// <summary>Exceeds a one-state frontier before copying additional logical paths.</summary>
    FrontierLimit,

    /// <summary>Distinguishes retained initial routing from the current manager's different routing value.</summary>
    RoutingSwitch,

    /// <summary>Gives the first manager an additional visible own membership for explicit re-expansion.</summary>
    ReExpansion,
}

/// <summary>Compares the actual batched chain with a complete independent per-path application chain.</summary>
/// <remarks>
/// Both paths retain the same structural candidate builder and server-side person-selection semantics.
/// Physical reader counts intentionally differ: the reference performs one bounded reader per logical path.
/// This is an orchestration comparison, not a claim of an independent whole-query SQL implementation.
/// </remarks>
[Config(typeof(DatabaseBenchmarkConfiguration))]
public class UserGroupChainBenchmarks : IDatabaseBenchmark, IAsyncDisposable
{
    private readonly CommandObservation _observation = new();
    private UserGroupContext? _context;
    private ChainBenchmarkFixture? _data;
    private SupervisorChainResult? _result;

    /// <summary>Gets or sets the actual persisted group population, including nine management levels.</summary>
    [ParamsSource(nameof(NodeCounts))]
    public int Nodes { get; set; }

    /// <summary>Gets or sets the measured auxiliary branch's deterministic persisted shape.</summary>
    [ParamsSource(nameof(Shapes))]
    public BenchmarkShape Shape { get; set; }

    /// <summary>Gets or sets the single actual group tree; user hierarchies are separate application payload.</summary>
    [Params(1)]
    public int Trees { get; set; }

    /// <summary>Gets or sets the empty tracker required for scalar chain execution.</summary>
    [Params(0)]
    public int Tracked { get; set; }

    /// <summary>Gets or sets the exact requested hop count, fan-out, or explicit termination profile.</summary>
    [ParamsSource(nameof(Profiles))]
    public BenchmarkChainProfile Profile { get; set; }

    /// <summary>Gets or sets explicit membership re-expansion instead of original-cursor continuation.</summary>
    [Params(false, true)]
    public bool ReExpandMemberships { get; set; }

    /// <summary>Gets or sets current-subject routing instead of retaining the initial routing owner.</summary>
    [Params(false, true)]
    public bool CurrentSubjectRouting { get; set; }

    /// <summary>Gets the unchanged launcher's explicit physical group populations.</summary>
    public static IEnumerable<int> NodeCounts => BenchmarkRunOptions.Current.NodeCounts;

    /// <summary>Gets the unchanged launcher's explicit auxiliary tree shapes.</summary>
    public static IEnumerable<BenchmarkShape> Shapes => BenchmarkRunOptions.Current.Shapes;

    /// <summary>Gets finite named cases including every 2/4/8-hop and 1/2/4-fan-out combination.</summary>
    public static IEnumerable<BenchmarkChainProfile> Profiles => Enum.GetValues<BenchmarkChainProfile>();

    /// <summary>Gets every measured request status, branch, cursor occurrence, and retained hop.</summary>
    internal SupervisorChainResult Result => _result ?? throw new InvalidOperationException("The chain did not run.");

    /// <summary>Gets seeded identities only for deterministic functional verification outside measurement.</summary>
    internal ChainBenchmarkFixture Data =>
        _data ?? throw new InvalidOperationException("Prepare the chain dataset first.");

    /// <summary>Gets the actual sample context for adversarial untimed benchmark contract tests.</summary>
    internal UserGroupContext Context =>
        _context ?? throw new InvalidOperationException("Initialize the chain benchmark first.");

    /// <summary>Configures the actual sample after the existing launcher establishes resource ownership.</summary>
    [GlobalSetup]
    public void Initialize()
    {
        if (Trees != 1
            || Tracked != 0
            || !Enum.IsDefined(Profile))
        {
            throw new ArgumentException(
                "Chain profiles require one group tree, an empty tracker, and a known finite profile.");
        }

        _context = new UserGroupContext(ConsumerBenchmarkDatabase.Options(_observation));
    }

    /// <summary>Completes asynchronous preparation before native measurement starts.</summary>
    [IterationSetup]
    public void Prepare() => BenchmarkLifecycle.Complete(PrepareAsync());

    /// <summary>Completes equivalence verification after native measurement stops.</summary>
    [IterationCleanup]
    public void Verify() => BenchmarkLifecycle.Complete(VerifyAsync());

    /// <summary>Seeds actual groups, users, memberships, roles, periods, and routing values outside timing.</summary>
    public async Task PrepareAsync()
    {
        await ConsumerBenchmarkDatabase.ResetAsync(Context);
        _data = await UserGroupChainBenchmarkData.CreateAsync(Context, Nodes, Shape, Profile);
        _result = null;
        _observation.Reset();
    }

    /// <summary>Runs the actual sample's deduplicated bounded-frontier chain within its qualified snapshot.</summary>
    [Benchmark]
    public async Task Api() => _result = await RunApiAsync(Request, CancellationToken.None);

    /// <summary>Runs the same complete contract with ordinary independent per-path application orchestration.</summary>
    [Benchmark(Baseline = true)]
    public async Task Reference()
    {
        _result = await UserGroupChainReference.FollowAsync(
            Context,
            Data.Tenant,
            Request,
            !ReExpandMemberships,
            CancellationToken.None);
    }

    /// <summary>Checks every unordered branch and scalar evidence against the independent orchestration.</summary>
    public async Task VerifyAsync()
    {
        var result = Result;
        var expected = await UserGroupChainReference.FollowAsync(
            Context,
            Data.Tenant,
            Request,
            !ReExpandMemberships,
            CancellationToken.None);

        BenchmarkFixture.Require(
            Signature(result)
                .SequenceEqual(Signature(expected)),
            "Batched and per-path chains differ in status, cursor, routing, or retained provenance.");
        BenchmarkFixture.Require(
            !Context
                .ChangeTracker
                .Entries()
                .Any(),
            "Scalar chain execution populated the tracker.");
    }

    /// <summary>Runs an explicit test request through the unchanged actual sample API.</summary>
    internal Task<SupervisorChainResult> RunApiAsync(
        SupervisorChainRequest request,
        CancellationToken cancellationToken
    ) => ReExpandMemberships
        ? SupervisorQueries.FollowAllMembershipsAsync(Context, Data.Tenant, request, cancellationToken)
        : SupervisorQueries.FollowOriginalPathAsync(Context, Data.Tenant, request, cancellationToken);

    /// <summary>Creates the exact routing request without mutating an earlier result or frontier.</summary>
    internal SupervisorChainRequest Request =>
        Data.Request with
        {
            RoutingOrigin = CurrentSubjectRouting ? RoutingOrigin.CurrentSubject : RoutingOrigin.InitialSubject,
        };

    /// <summary>Releases the context while the existing launcher retains database/container ownership.</summary>
    [GlobalCleanup]
    public async Task CleanupAsync() => await DisposeAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
            _context = null;
        }

        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    CommandObservation IDatabaseBenchmark.Observation => _observation;

    /// <summary>Orders presentation while preserving duplicate paths and every scalar evidence field.</summary>
    internal static string[] Signature(
        SupervisorChainResult result
    ) => new[] { $"{result.Status}:{result.Complete}" }
        .Concat(
            result
                .Branches
                .Select(branch => $"{branch.Status}:{branch.InitialGroupId}|"
                    + string.Join(
                        "/",
                        branch.Hops.Select(hop =>
                            $"{hop.Number}:{hop.InitialUserId}:{hop.InitialGroupId}:{hop.InitialTreeId}:"
                            + $"{hop.Evidence.TenantId}:{hop.Evidence.SourceUserId}:{hop.Evidence.StartGroupId}:"
                            + $"{hop.Evidence.StartTreeId}:{hop.Evidence.FoundGroupId}:{hop.Evidence.CandidateUserId}:"
                            + $"{hop.Evidence.Distance}:{hop.Evidence.LeadershipPriority}")))
                .Order(StringComparer.Ordinal))
        .ToArray();
}

using Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Names person-selection cases without exposing sample-internal enums in a public signature.</summary>
public enum BenchmarkPersonSelection
{
    /// <summary>Requires exactly one person per source membership.</summary>
    RequireUnique,

    /// <summary>Chooses the unique highest-priority person, retaining ties as ambiguous.</summary>
    HighestBusinessPriority,

    /// <summary>Requests the second distinct business-ranked person, preserving rank ties.</summary>
    SecondPerson,
}

/// <summary>Compares server person reduction with equivalent application reduction of all candidates.</summary>
/// <remarks>
/// Api and Reference compare complete consumers. Prepared isolates execution of an asynchronously warmed EF
/// query; its application result reconstruction and equivalence verification run outside measurement.
/// </remarks>
[Config(typeof(DatabaseBenchmarkConfiguration))]
public class UserGroupSelectionBenchmarks : IDatabaseBenchmark, IAsyncDisposable
{
    private readonly CommandObservation _observation = new();
    private UserGroupContext? _context;
    private SupervisorScenarioFixture? _data;
    private SupervisorRequest? _request;
    private SupervisorResolutionBatch? _result;
    private IQueryable<SupervisorQueries.SupervisorReadRow>? _prepared;
    private SupervisorQueries.SupervisorReadRow[]? _preparedRows;

    /// <summary>Gets or sets the actual group count; users remain a fixed separate payload population.</summary>
    [ParamsSource(nameof(NodeCounts))]
    public int Nodes { get; set; }

    /// <summary>Gets or sets the shape of the measured group branch attached to the sample's management root.</summary>
    [ParamsSource(nameof(Shapes))]
    public BenchmarkShape Shape { get; set; }

    /// <summary>Gets or sets the single group tree; the separate user hierarchy is application payload.</summary>
    [Params(1)]
    public int Trees { get; set; }

    /// <summary>Gets or sets the empty tracker used by scalar selection.</summary>
    [Params(0)]
    public int Tracked { get; set; }

    /// <summary>Gets or sets the actual number of visible own memberships selected by the request.</summary>
    [Params(1, 16, 100, 1_000)]
    public int Origins { get; set; }

    /// <summary>Gets or sets the number of same-candidate metadata comparisons.</summary>
    [Params(1, 4, 16)]
    public int Matches { get; set; }

    /// <summary>Gets or sets the complete person-selection semantics used by every measured path.</summary>
    [Params(
        BenchmarkPersonSelection.RequireUnique,
        BenchmarkPersonSelection.HighestBusinessPriority,
        BenchmarkPersonSelection.SecondPerson)]
    public BenchmarkPersonSelection Selection { get; set; }

    /// <summary>Gets node populations from the unchanged launcher configuration.</summary>
    public static IEnumerable<int> NodeCounts => BenchmarkRunOptions.Current.NodeCounts;

    /// <summary>Gets deterministic branch shapes from the unchanged launcher configuration.</summary>
    public static IEnumerable<BenchmarkShape> Shapes => BenchmarkRunOptions.Current.Shapes;

    /// <summary>Gets the actual request-level and per-origin outcome for functional benchmark verification.</summary>
    internal SupervisorResolutionBatch Result =>
        _result ?? throw new InvalidOperationException("The supervisor query did not run.");

    /// <summary>Configures the real sample context after the launcher has acquired its database.</summary>
    [GlobalSetup]
    public void Initialize()
    {
        if (Trees != 1
            || Tracked != 0)
        {
            throw new ArgumentException("The selection benchmark has one group tree and an empty tracker.");
        }

        _context = new UserGroupContext(ConsumerBenchmarkDatabase.Options(_observation));
    }

    /// <summary>Completes actual sample preparation before native measurement begins.</summary>
    [IterationSetup]
    public void Prepare() => BenchmarkLifecycle.Complete(PrepareAsync());

    /// <summary>Completes equivalence verification outside native measurement.</summary>
    [IterationCleanup]
    public void Verify() => BenchmarkLifecycle.Complete(VerifyAsync());

    /// <summary>Seeds inputs and asynchronously warms selected/reference EF execution before observation.</summary>
    public async Task PrepareAsync()
    {
        await ConsumerBenchmarkDatabase.ResetAsync(Context);
        (_data, _request) = await UserGroupBenchmarkData.CreateAsync(Context, Nodes, Shape, Origins, Matches);
        _result = null;
        _preparedRows = null;
        _prepared = SupervisorQueries.ResolutionRows(Context, Data.TenantId, Request, PersonSelection);

        // WHY: Actual async execution warms EF's execution cache; ToQueryString warms a different path.
        // Every workload receives the same warm selected/reference shapes before operation counters reset.
        _ = await _prepared.ToArrayAsync(CancellationToken.None);
        _ = await ReadReferenceAsync();
        _observation.Reset();
    }

    /// <summary>Runs the actual server-reduced source/status/evidence reader.</summary>
    [Benchmark]
    public async Task Api()
    {
        _result = await SupervisorQueries.ResolveAsync(
            Context,
            Data.TenantId,
            Request,
            PersonSelection,
            CancellationToken.None);
    }

    /// <summary>Reads complete candidates/statuses and applies identical application person selection.</summary>
    [Benchmark(Baseline = true)]
    public async Task Reference() => _result = await ReadReferenceAsync();

    /// <summary>Measures prepared asynchronous scalar execution and materialization, excluding reduction.</summary>
    [Benchmark]
    public async Task Prepared()
    {
        var query = _prepared ?? throw new InvalidOperationException("Prepare the supervisor query first.");
        _preparedRows = await query.ToArrayAsync(CancellationToken.None);
    }

    /// <summary>Checks origin statuses, retained evidence and canonical continuations after timing.</summary>
    public async Task VerifyAsync()
    {
        if (_preparedRows is { } rows)
        {
            _result = SupervisorQueries.ReadResolution(Data.TenantId, rows, PersonSelection);
        }

        var actual = Result;
        var expected = await ReadReferenceAsync();
        BenchmarkFixture.Require(
            Signature(actual)
                .SequenceEqual(Signature(expected)),
            "Server and application person reductions produced different statuses or provenance.");
        BenchmarkFixture.Require(actual.Origins.Count == Origins, "The request lost an evaluated source origin.");
        BenchmarkFixture.Require(
            !Context
                .ChangeTracker
                .Entries()
                .Any(),
            "Scalar supervisor selection populated the tracker.");
    }

    /// <summary>Releases the actual sample context; resource ownership remains with the existing launcher.</summary>
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

        _prepared = null;
        _preparedRows = null;
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    CommandObservation IDatabaseBenchmark.Observation => _observation;

    /// <summary>Retains the source/catalog/zero-origin contract instead of timing an incomplete DTO query.</summary>
    private async Task<SupervisorResolutionBatch> ReadReferenceAsync()
    {
        var all = await SupervisorQueries.ResolveAsync(
            Context,
            Data.TenantId,
            Request,
            SupervisorSelection.All,
            CancellationToken.None);

        return all with
        {
            Origins = all
                .Origins
                .Select(origin => SupervisorSelectionRules.Select(
                    origin.StartGroupId!.Value,
                    origin.Evidence,
                    PersonSelection))
                .ToArray(),
        };
    }

    /// <summary>Normalizes scalar result ordering while retaining every independent provenance occurrence.</summary>
    private static IEnumerable<string> Signature(
        SupervisorResolutionBatch batch
    ) => new[] { $"{batch.Status}:{batch.Complete}" }.Concat(
        batch
            .Origins
            .OrderBy(origin => origin.StartGroupId)
            .SelectMany(origin => new[]
            {
                $"{origin.StartGroupId}:{origin.Status}:{origin.Continuation?.FoundGroupId}:"
                + $"{origin.Continuation?.CandidateUserId}",
            }.Concat(
                origin
                    .Evidence
                    .OrderBy(row => row.FoundGroupId)
                    .ThenBy(row => row.CandidateUserId)
                    .Select(row => $"{row.StartGroupId}:{row.FoundGroupId}:{row.CandidateUserId}:"
                        + $"{row.Distance}:{row.LeadershipPriority}:{row.StartTreeId}"))));

    /// <summary>Maps the public benchmark case to the unchanged sample-owned selection contract.</summary>
    private SupervisorSelection PersonSelection =>
        Selection switch
        {
            BenchmarkPersonSelection.RequireUnique => SupervisorSelection.RequireUnique,
            BenchmarkPersonSelection.HighestBusinessPriority => SupervisorSelection.HighestBusinessPriority,
            BenchmarkPersonSelection.SecondPerson => SupervisorSelection.NthPerson(2),
            _ => throw new ArgumentOutOfRangeException(nameof(Selection)),
        };

    /// <summary>Rejects operation outside the native benchmark lifecycle.</summary>
    private UserGroupContext Context =>
        _context ?? throw new InvalidOperationException("Initialize the group benchmark first.");

    /// <summary>Requires a completed actual sample seed.</summary>
    private SupervisorScenarioFixture Data =>
        _data ?? throw new InvalidOperationException("Prepare the group dataset first.");

    /// <summary>Requires the privately captured explicit consumer request.</summary>
    private SupervisorRequest Request =>
        _request ?? throw new InvalidOperationException("Prepare the group request first.");
}

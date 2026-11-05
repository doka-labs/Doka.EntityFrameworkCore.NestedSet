namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures bounded imports with absent, empty, or fully prepared external owner collections.</summary>
/// <remarks>
/// These are current-contract observations. Earlier versions rejected complete future-wave collections, so
/// successful prepared imports are compared with identical current writes without prepared membership.
/// </remarks>
[Config(typeof(DatabaseBenchmarkConfiguration))]
public class PreparedOwnerImportBenchmarks : IDatabaseBenchmark, IAsyncDisposable
{
    private static readonly Guid s_seedTree = new(1, 0, 0, new byte[8]);
    private static readonly Guid s_firstTree = new(-1, 0, 0, new byte[8]);
    private static readonly Guid s_secondTree = new(-2, 0, 0, new byte[8]);
    private readonly CommandObservation _observation = new();
    private NestedSetBranch<PreparedOwnerNode>? _branch;
    private ImportOperation _completed;
    private PreparedOwnerContext? _context;
    private int _detectionPasses;
    private NestedSetTreeImport<PreparedOwnerNode, Guid>[] _forest = [];
    private PreparedOwnerNode[] _inputs = [];
    private PreparedOwner? _owner;

    /// <summary>Gets or sets the explicitly selected import size, including multi-wave scaling cases.</summary>
    [ParamsSource(nameof(NodeCounts))]
    public int Nodes { get; set; }

    /// <summary>
    /// Gets import sizes from the existing launcher settings, including 65/130-row boundary controls.
    /// </summary>
    public static IEnumerable<int> NodeCounts => BenchmarkRunOptions.Current.NodeCounts;

    /// <summary>Gets or sets the wide deterministic branch used to isolate owner membership work.</summary>
    [Params(BenchmarkShape.Wide)]
    public BenchmarkShape Shape { get; set; }

    /// <summary>Gets or sets the single tree seeded before the measured import.</summary>
    [Params(1)]
    public int Trees { get; set; }

    /// <summary>Gets or sets the absent unrelated tracker population; owner tracking is selected separately.</summary>
    [Params(0)]
    public int Tracked { get; set; }

    /// <summary>Gets or sets whether the same persisted owner is untracked, loaded, or fully prepared.</summary>
    [Params(PreparedOwnerMode.Untracked, PreparedOwnerMode.LoadedEmpty, PreparedOwnerMode.Prepared)]
    public PreparedOwnerMode OwnerMode { get; set; }

    /// <summary>Gets the exact model used by functional fixture verification.</summary>
    internal PreparedOwnerContext Context =>
        _context ?? throw new InvalidOperationException("Initialize the prepared-owner benchmark first.");

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
            _context = null;
        }

        _owner = null;
        _inputs = [];
        _branch = null;
        _forest = [];
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    CommandObservation IDatabaseBenchmark.Observation => _observation;

    /// <summary>Acquires the existing launcher's provider without creating a second resource owner.</summary>
    [GlobalSetup]
    public void Initialize()
    {
        if (Nodes < 10
            || Shape != BenchmarkShape.Wide
            || Trees != 1
            || Tracked != 0
            || !Enum.IsDefined(OwnerMode))
        {
            throw new ArgumentException("Prepared-owner imports require at least ten nodes and the declared topology.");
        }

        _context = new PreparedOwnerContext(ConsumerBenchmarkDatabase.Options(_observation));
        _context.ChangeTracker.DetectedAllChanges += (_, _) => _detectionPasses++;
    }

    /// <summary>Creates identical persisted rows and detached plans for all three owner states.</summary>
    /// <param name="cancellationToken">Cancellation for the operation's preparation or verification work.</param>
    public async Task PrepareAsync(
        CancellationToken cancellationToken
    )
    {
        _completed = ImportOperation.None;
        _owner = null;
        Context.ChangeTracker.AutoDetectChangesEnabled = true;
        Context.ChangeTracker.Clear();
        await ConsumerBenchmarkDatabase.ResetAsync(Context, cancellationToken);
        await Context.Owners.AddAsync(
            new PreparedOwner
            {
                Id = 1,
                Payload = "original",
            },
            cancellationToken);

        await Context.SaveChangesAsync(cancellationToken);
        Context.ChangeTracker.Clear();
        await Context
            .NestedSet<PreparedOwnerNode>()
            .ForScope(1)
            .InsertRootAsync(
                new PreparedOwnerNode
                {
                    Id = 1,
                    Name = "Seed",
                },
                s_seedTree,
                cancellationToken);

        Context.ChangeTracker.Clear();
        Context.ChangeTracker.AutoDetectChangesEnabled = false;

        if (OwnerMode != PreparedOwnerMode.Untracked)
        {
            _owner = await Context.Owners.SingleAsync(cancellationToken);
        }

        _inputs = Enumerable
            .Range(0, Nodes)
            .Select(index => new PreparedOwnerNode
            {
                Id = index + 2,
                Name = FormattableString.Invariant($"Input-{index:D3}"),
            })
            .ToArray();

        _branch = Branch(0, Nodes);
        var split = (Nodes + 1) / 2;
        _forest =
        [
            new NestedSetTreeImport<PreparedOwnerNode, Guid>(s_firstTree, Branch(0, split)),
            new NestedSetTreeImport<PreparedOwnerNode, Guid>(s_secondTree, Branch(split, Nodes - split)),
        ];

        if (OwnerMode == PreparedOwnerMode.Prepared)
        {
            // WHY: Prepare every validated input before the first wave; none is attached by this raw CLR edit.
            _owner!.Nodes.AddRange(_inputs);
        }

        _detectionPasses = 0;
        _observation.Reset();
    }

    /// <summary>Checks exact persisted topology, owner reuse, full validation, and an ordinary marked save.</summary>
    /// <param name="cancellationToken">Cancellation for the operation's preparation or verification work.</param>
    public async Task VerifyAsync(
        CancellationToken cancellationToken
    )
    {
        BenchmarkFixture.Require(_completed != ImportOperation.None, "The prepared-owner import did not execute.");
        BenchmarkFixture.Require(
            !Context.ChangeTracker.AutoDetectChangesEnabled && _detectionPasses == 0,
            "The prepared-owner import detected unrelated changes.");

        var rows = await Context
            .Nodes
            .AsNoTracking()
            .OrderBy(node => node.Id)
            .ToArrayAsync(cancellationToken);

        BenchmarkFixture.Require(rows.Length == Nodes + 1, "The import persisted an unexpected row count.");
        var split = (Nodes + 1) / 2;

        // WHY: The input keys define a separate flat adjacency oracle; bounds are checked by full validation below.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < Nodes; index++)
        {
            var row = rows[index + 1];
            var second = index >= split;
            int? expectedParent = _completed == ImportOperation.Subtree
                ? index == 0
                    ? 1
                    : 2
                : index == 0 || index == split
                    ? null
                    : second
                        ? split + 2
                        : 2;

            var expectedTree = _completed == ImportOperation.Subtree
                ? s_seedTree
                : second
                    ? s_secondTree
                    : s_firstTree;

            BenchmarkFixture.Require(
                row.Id == index + 2 && row.Scope == 1 && row.ParentId == expectedParent && row.TreeId == expectedTree,
                "The import changed the independent adjacency or tree assignment.");
        }

        foreach (var treeId in rows
            .Select(node => node.TreeId)
            .Distinct())
        {
            var report = await Context
                .NestedSet<PreparedOwnerNode>()
                .ForScope(1)
                .InTree(treeId)
                .ValidateAsync(NestedSetValidationLevel.Full, cancellationToken);

            BenchmarkFixture.Require(report.IsValid, "The imported hierarchy failed full validation.");
        }

        if (_owner is not null)
        {
            BenchmarkFixture.Require(
                _owner.Nodes.Count == Nodes
                && _owner
                    .Nodes
                    .ToHashSet(ReferenceEqualityComparer.Instance)
                    .SetEquals(_inputs),
                "The owner lost or duplicated an imported input reference.");
        }

        _owner ??= await Context.Owners.SingleAsync(cancellationToken);
        Context
            .Entry(_owner)
            .Property(owner => owner.Payload)
            .CurrentValue = "following save";

        await Context.SaveChangesAsync(cancellationToken);

        var payload = await Context
            .Owners
            .AsNoTracking()
            .Select(owner => owner.Payload)
            .SingleAsync(cancellationToken);

        BenchmarkFixture.Require(
            payload == "following save" && !Context.ChangeTracker.AutoDetectChangesEnabled && _detectionPasses == 0,
            "The saved owner could not be reused without automatic detection.");
    }

    /// <summary>Releases only the child-owned context; the existing launcher owns the database.</summary>
    [GlobalCleanup]
    public async Task CleanupAsync() => await DisposeAsync();

    /// <summary>Completes setup before timing and allocation observations begin.</summary>
    [IterationSetup]
    public void Prepare() => BenchmarkLifecycle.Complete(PrepareAsync(CancellationToken.None));

    /// <summary>Completes functional and follow-up-save checks outside the observation window.</summary>
    [IterationCleanup]
    public void Verify() => BenchmarkLifecycle.Complete(VerifyAsync(CancellationToken.None));

    /// <summary>Imports one complete prepared branch through several bounded waves.</summary>
    [Benchmark]
    public async Task Subtree()
    {
        var branch = _branch ?? throw new InvalidOperationException("Prepare the detached branch first.");
        await Context
            .NestedSet<PreparedOwnerNode>()
            .ForScope(1)
            .InsertSubtreeAsync(branch, 1, CancellationToken.None);

        _completed = ImportOperation.Subtree;
    }

    /// <summary>Imports two independent prepared plans while their shared owner exposes both complete inputs.</summary>
    [Benchmark]
    public async Task Forest()
    {
        await Context
            .NestedSet<PreparedOwnerNode>()
            .ForScope(1)
            .InsertForestAsync(_forest, CancellationToken.None);

        _completed = ImportOperation.Forest;
    }

    /// <summary>Creates one wide branch without setting non-owned CLR navigation backreferences.</summary>
    private NestedSetBranch<PreparedOwnerNode> Branch(
        int start,
        int count
    ) => new(
        _inputs[start],
        _inputs
            .Skip(start + 1)
            .Take(count - 1)
            .Select(node => new NestedSetBranch<PreparedOwnerNode>(node))
            .ToArray());

    /// <summary>Distinguishes verified import effects from an unexecuted workload.</summary>
    private enum ImportOperation
    {
        None,
        Subtree,
        Forest,
    }
}

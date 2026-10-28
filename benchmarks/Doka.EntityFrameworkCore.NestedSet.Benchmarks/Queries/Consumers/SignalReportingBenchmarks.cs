extern alias SignalSample;
using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;
using ReadSnapshot = SignalSample::Doka.EntityFrameworkCore.NestedSet.Samples.SampleReadSnapshot;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures the actual SignalLab sample and an equivalent explicit-LINQ report on identical prepared data.</summary>
public class SignalReportingBenchmarks : IDatabaseBenchmark, IAsyncDisposable
{
    private readonly CommandObservation _observation = new();
    private SignalLabContext? _context;
    private SignalReportRequest? _request;
    private ValidatedSignalReport? _validated;
    private SignalReportCell[]? _result;

    /// <summary>Gets or sets the actual persisted node count, including fifteen sample controls.</summary>
    [ParamsSource(nameof(NodeCounts))]
    public int Nodes { get; set; }

    /// <summary>Gets or sets the deterministic shape of the added measured branch.</summary>
    [ParamsSource(nameof(Shapes))]
    public BenchmarkShape Shape { get; set; }

    /// <summary>Gets or sets the actual three isolation trees created by the sample dataset.</summary>
    [Params(3)]
    public int Trees { get; set; }

    /// <summary>Gets or sets the empty tracker required for this scalar reporting comparison.</summary>
    [Params(0)]
    public int Tracked { get; set; }

    /// <summary>Gets or sets whether the same metadata uses Any or nearest-before-value semantics.</summary>
    [Params(false, true)]
    public bool NearestMetadata { get; set; }

    /// <summary>Gets or sets unused allowed values under one requested field to expose catalog transport cost.</summary>
    [Params(0, 1000)]
    public int IrrelevantChoices { get; set; }

    /// <summary>Gets the existing launcher's explicitly selected actual node populations.</summary>
    public static IEnumerable<int> NodeCounts => BenchmarkRunOptions.Current.NodeCounts;

    /// <summary>Gets the existing launcher's explicitly selected added-branch shapes.</summary>
    public static IEnumerable<BenchmarkShape> Shapes => BenchmarkRunOptions.Current.Shapes;

    /// <summary>Gets the actual materialized scalar report for functional benchmark assertions.</summary>
    internal IReadOnlyList<SignalReportCell> Result =>
        _result ?? throw new InvalidOperationException("The SignalLab report did not run.");

    /// <summary>Configures the real sample context after launcher resource ownership is established.</summary>
    public void Initialize()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(IrrelevantChoices);

        if (Trees != 3
            || Tracked != 0)
        {
            throw new ArgumentException("The SignalLab dataset has three isolation trees and an empty tracker.");
        }

        _context = new SignalLabContext(ConsumerBenchmarkDatabase.Options(_observation));
    }

    /// <summary>Prepares one immutable persisted dataset before adaptive read-throughput measurement.</summary>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        // WHY: Reports do not mutate their fixture. Resetting it per iteration forces single invocations,
        // preventing BenchmarkDotNet from amortizing timer overhead across sufficiently long iterations.
        Initialize();
        await PrepareAsync();
    }

    /// <summary>Prepares the real catalog, three fact tables, metadata and direct-edge periods.</summary>
    public async Task PrepareAsync()
    {
        await ConsumerBenchmarkDatabase.ResetAsync(Context);
        var data = await SignalBenchmarkData.CreateAsync(Context, Nodes, Shape);
        _request = data.Request(NearestMetadata ? TagOwnerMode.Nearest : TagOwnerMode.Any);

        if (IrrelevantChoices > 0)
        {
            var criterion = _request.Criteria[0];

            for (var index = 0; index < IrrelevantChoices; index++)
            {
                // WHY: These valid choices belong to a requested field but are never requested values.
                // Varying only this catalog population exposes excess transport without changing any report cell.
                Context.TagChoices.Add(
                    new TagChoice
                    {
                        TenantId = _request.TenantId,
                        TypeId = criterion.TypeId,
                        FieldCode = criterion.FieldCode,
                        Value = FormattableString.Invariant($"Unused-{index:D4}"),
                    });
            }

            await Context.SaveChangesAsync(CancellationToken.None);
            Context.ChangeTracker.Clear();
        }

        _validated = SignalReportValidation.Snapshot(_request);
        _result = null;
        _observation.Reset();
    }

    /// <summary>Executes the actual sample's batched catalog and scalar report within its qualified snapshot.</summary>
    [Benchmark]
    public async Task Api()
    {
        var request = _request ?? throw new InvalidOperationException("Prepare the SignalLab request first.");
        _result = await SignalReportBuilder.ReadAsync(Context, request, CancellationToken.None);
    }

    /// <summary>Executes the same catalog, snapshot and report using explicit mapping-specific LINQ.</summary>
    [Benchmark(Baseline = true)]
    public async Task Reference()
    {
        var input = _request ?? throw new InvalidOperationException("Prepare the SignalLab request first.");
        // WHY: The API takes an owned snapshot on each read. The reference must pay for the same request
        // validation and copying inside its measured invocation rather than consuming setup-only work.
        var request = SignalReportValidation.Snapshot(input);
        _result = await ReadReferenceAsync(Context, request);
    }

    /// <summary>Rejects unexecuted operations and unequal cells, including missing-versus-zero distinctions.</summary>
    public async Task VerifyAsync()
    {
        var actual = Result;
        var request = _validated ?? throw new InvalidOperationException("Prepare the SignalLab request first.");
        var expected = await ReadReferenceAsync(Context, request);
        var input = _request ?? throw new InvalidOperationException("Prepare the SignalLab request first.");
        var api = await SignalReportBuilder.ReadAsync(Context, input, CancellationToken.None);

        BenchmarkFixture.Require(
            Normalize(actual)
                .SequenceEqual(Normalize(expected)),
            "SignalLab source, period, metadata, or per-anchor report semantics differ from the reference.");

        BenchmarkFixture.Require(
            Normalize(api)
                .SequenceEqual(Normalize(expected)),
            "The SignalLab API and reference disagree after repeated read-only invocations.");

        BenchmarkFixture.Require(
            !Context
                .ChangeTracker
                .Entries()
                .Any(),
            "SignalLab scalar reporting populated the tracker.");
    }

    /// <summary>Verifies complete API and reference reports outside timing, then releases the context.</summary>
    [GlobalCleanup]
    public async Task VerifyAndCleanupAsync()
    {
        try
        {
            await VerifyAsync();
        }
        finally
        {
            await DisposeAsync();
        }
    }

    /// <summary>Releases the actual sample context while the launcher retains database ownership.</summary>
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

    /// <summary>Uses the sample's exact read-transaction and catalog-validation contract in the reference.</summary>
    private static Task<SignalReportCell[]> ReadReferenceAsync(
        SignalLabContext context,
        ValidatedSignalReport request
    ) => ReadSnapshot.RunAsync(
        context,
        async token =>
        {
            var catalog = await SignalReportBuilder
                .Catalog(context, request)
                .ToArrayAsync(token);

            SignalReportValidation.RequireCatalog(request, catalog);

            var totals = await SignalReportReference
                .Build(context, request)
                .ToArrayAsync(token);

            return SignalReportValidation.CreateGrid(request, totals);
        },
        CancellationToken.None);

    /// <summary>Compares unordered SQL report cells without imposing a measured presentation sort.</summary>
    private static IEnumerable<SignalReportCell> Normalize(
        IReadOnlyList<SignalReportCell> cells
    ) => cells
        .OrderBy(cell => cell.AnchorId)
        .ThenBy(cell => cell.ChannelId)
        .ThenBy(cell => cell.Source);

    /// <summary>Rejects use outside the existing native benchmark lifecycle.</summary>
    internal SignalLabContext Context =>
        _context ?? throw new InvalidOperationException("Initialize the SignalLab benchmark first.");
}

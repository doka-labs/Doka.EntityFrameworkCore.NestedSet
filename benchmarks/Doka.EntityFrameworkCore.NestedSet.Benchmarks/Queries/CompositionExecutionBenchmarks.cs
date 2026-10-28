namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures prepared relational execution separately from construction and fixture seeding.</summary>
/// <remarks>Native iterations and allocations are observational; no timing threshold becomes a gate.</remarks>
// WHY: BenchmarkDotNet generates a derived execution type in its isolated child process.
public class CompositionExecutionBenchmarks : DatabaseBenchmark
{
    private IQueryable<CompositionBenchmarkTotal>? _api;
    private IQueryable<CompositionBenchmarkTotal>? _reference;
    private CompositionBenchmarkTotal[]? _result;

    /// <summary>Gets or sets the anchor count, capped by the prepared first tree's population.</summary>
    [Params(1, 10, 100, 1_000)]
    public int Anchors { get; set; }

    /// <summary>Gets or sets the number of selective payload nodes, capped by the first tree's population.</summary>
    [Params(1, 100)]
    public int PayloadRows { get; set; }

    /// <summary>Gets the actual grouped scalar output for independent functional benchmark tests.</summary>
    internal IReadOnlyList<CompositionBenchmarkTotal> Result =>
        _result ?? throw new InvalidOperationException("The composition query did not run.");

    /// <summary>Prepares both equivalent query expressions after the ordinary isolated database seed.</summary>
    public override async Task PrepareAsync()
    {
        await base.PrepareAsync();
        _result = null;
        var treeId = Fixture.Forest.Imports[0].TreeId;
        _api = CompositionBenchmarkQueries.Api(Fixture.Context, treeId, Fixture.LeafKey, Anchors, PayloadRows);
        _reference = CompositionBenchmarkQueries.Reference(
            Fixture.Context,
            treeId,
            Fixture.LeafKey,
            Anchors,
            PayloadRows);
    }

    /// <summary>Executes the equivalent ordinary LINQ reference against the prepared data.</summary>
    [Benchmark(Baseline = true)]
    public async Task Reference()
    {
        var query = _reference ?? throw new InvalidOperationException("Prepare the composition queries first.");
        _result = await query.ToArrayAsync(CancellationToken.None);
    }

    /// <summary>Executes the prepared library expression with a single asynchronous scalar result reader.</summary>
    [Benchmark]
    public async Task Api()
    {
        var query = _api ?? throw new InvalidOperationException("Prepare the composition queries first.");
        _result = await query.ToArrayAsync(CancellationToken.None);
    }

    /// <summary>Checks the exact independent adjacency aggregate and tracker state outside measured work.</summary>
    public override async Task VerifyAsync()
    {
        var firstAnchor = Math.Max(2, Fixture.LeafKey - Anchors + 2);
        var firstPayload = Math.Max(1, Fixture.LeafKey - PayloadRows + 1);
        var totals = new SortedDictionary<int, (long Count, long Sum)>();

        for (var key = firstPayload; key <= Fixture.LeafKey; key++)
        {
            int? current = key;

            while (current is { } ancestor)
            {
                if (ancestor == 1
                    || ancestor >= firstAnchor)
                {
                    var previous = totals.GetValueOrDefault(ancestor);
                    totals[ancestor] = (previous.Count + 1, previous.Sum + key);
                }

                current = Fixture.Forest.Parents[ancestor - 1];
            }
        }

        var expected = totals.Select(pair => new CompositionBenchmarkTotal(pair.Key, pair.Value.Count, pair.Value.Sum));
        BenchmarkFixture.Require(Result.SequenceEqual(expected), "Composition changed anchor/payload multiplicity.");
        BenchmarkFixture.Require(
            !Fixture
                .Context
                .ChangeTracker
                .Entries<BenchmarkNode>()
                .Any(),
            "Scalar composition unexpectedly materialized tracked hierarchy nodes.");

        await base.VerifyAsync();
    }
}

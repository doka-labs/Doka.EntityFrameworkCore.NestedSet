using Microsoft.EntityFrameworkCore.Storage;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>
/// Observes complete native indexed fact reads, including provider reader and occurrence materialization.
/// </summary>
[Config(typeof(DatabaseBenchmarkConfiguration))]
public class AncestorFactReadBenchmarks : IDatabaseBenchmark, IAsyncDisposable
{
    private NativeIndexedContext? _context;
    private IQueryable<NativeFactPair>? _query;
    private NativeFactPair[]? _result;
    private readonly CommandObservation _observation = new();

    /// <summary>Gets or sets an original native indexed read profile.</summary>
    [ParamsSource(nameof(Cases))]
    public NativeFactCase Case { get; set; } = new("Fact", 100_000, false, false, true);

    /// <summary>Gets all fact, union, missing, million-node and consumer membership read profiles.</summary>
    public static IEnumerable<NativeFactCase> Cases => NativeFactCase.All;

    /// <inheritdoc />
    CommandObservation IDatabaseBenchmark.Observation => _observation;

    /// <summary>Rejects unsupported engines before preparing a native indexed read.</summary>
    [GlobalSetup]
    public void Initialize()
    {
        ArgumentNullException.ThrowIfNull(Case);

        if (BenchmarkRunOptions.Current.Engine is not (BenchmarkEngine.MySql or BenchmarkEngine.MariaDb))
        {
            throw new ArgumentException("Native ancestor fact reads require MySQL or MariaDB.");
        }
    }

    /// <summary>Recreates identical physical rows and statistics before each observed read.</summary>
    [IterationSetup]
    public void Prepare() => BenchmarkLifecycle.Complete(PrepareAsync(CancellationToken.None));

    /// <summary>Reads and materializes the complete occurrence bag exactly once.</summary>
    [Benchmark]
    public async Task Read()
    {
        _result = await (_query ?? throw new InvalidOperationException("Prepare the indexed fact query first."))
            .ToArrayAsync(CancellationToken.None);
    }

    /// <summary>Compares every returned occurrence and ancestor against independent adjacency.</summary>
    [IterationCleanup]
    public void Verify() => BenchmarkLifecycle.Complete(VerifyAsync(CancellationToken.None));

    /// <inheritdoc />
    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        await CleanupAsync();
        Initialize();
        var builder = new DbContextOptionsBuilder<NativeIndexedContext>();
        BenchmarkEnvironment.ConfigureProvider(
            builder,
            BenchmarkRunOptions.Current.Engine,
            BenchmarkEnvironment.ConnectionString);

        if (BenchmarkRunOptions.Current.Diagnostics)
        {
            builder.AddInterceptors(_observation);
        }

        var context = new NativeIndexedContext(builder.Options);
        _context = context;
        await context.Database.EnsureDeletedAsync(cancellationToken);
        await context.Database.EnsureCreatedAsync(cancellationToken);
        await context.Database.OpenConnectionAsync(cancellationToken);
        await SeedNodesAsync(context, cancellationToken);
        await SeedFactsAsync(context, cancellationToken);
        _query = NativeFactQueries.Build(context, Case, NativeFactQueries.Selected(Case));
        _observation.Reset();
    }

    /// <inheritdoc />
    public Task VerifyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = _result ?? throw new InvalidOperationException("The indexed fact read was not executed.");
        var expected = Expected().OrderBy(row => row.Occurrence).ThenBy(row => row.Owner).ToArray();
        var actual = result.OrderBy(row => row.Occurrence).ThenBy(row => row.Owner).ToArray();
        BenchmarkFixture.Require(actual.SequenceEqual(expected),
            "The indexed fact read changed identity, distance or multiplicity.");
        BenchmarkFixture.Require(!_context!.ChangeTracker.Entries().Any(),
            "The scalar indexed read tracked an entity.");

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    [GlobalCleanup]
    public async Task CleanupAsync() => await DisposeAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            _query = null;
            _result = null;

            if (_context is { } context)
            {
                _context = null;
                await context.DisposeAsync();
            }
        }
        finally
        {
            // WHY: Finalization suppression belongs to the owned lifecycle even when cleanup fails.
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Seeds exact star or chain coordinates in bounded batches without materializing a million entities.
    /// </summary>
    private async Task SeedNodesAsync(NativeIndexedContext context, CancellationToken cancellationToken)
    {
        var entity = context.Model.FindEntityType(typeof(NativeIndexedNode))!;
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var helper = context.GetService<ISqlGenerationHelper>();
        var caption = helper.DelimitIdentifier(entity.FindNavigation(nameof(NativeIndexedNode.Details))!
            .TargetEntityType.FindProperty(nameof(NativeIndexedDetail.Caption))!.GetColumnName(store)!);
        string[] names = ["Scope", "Id", "TreeId", "ParentId", "Left", "Right", "Depth", "Position", "Visible"];
        var columns = string.Join(",", names
            .Select(name => helper.DelimitIdentifier(entity.FindProperty(name)!.GetColumnName(store)!))
            .Append(caption));
        var table = helper.DelimitIdentifier(store.Name, store.Schema);
        await using var parameterCommand = context.Database.GetDbConnection().CreateCommand();

        // WHY: Fixed 1,000-row setup batches bound temporary memory and preserve the original physical population.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var start = 1; start <= Case.Nodes; start += 1_000)
        {
            var sql = new StringBuilder($"INSERT INTO {table} ({columns}) VALUES ");

            // WHY: Assigned keys are also the independent adjacency plan; no observed product bounds are reused.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var id = start; id < Math.Min(start + 1_000, Case.Nodes + 1); id++)
            {
                if (id != start)
                {
                    sql.Append(',');
                }

                var depth = Case.Chain ? id - 1 : id == 1 ? 0 : 1;
                var parent = id == 1 ? "NULL" : Case.Chain ? (id - 1).ToString(CultureInfo.InvariantCulture) : "1";
                var left = Case.Chain ? id : id == 1 ? 1 : 2L * id - 2;
                var right = Case.Chain ? 2L * Case.Nodes - depth : id == 1 ? 2L * Case.Nodes : left + 1;
                var position = Case.Chain ? 0 : Math.Max(0, id - 2);
                sql.Append(CultureInfo.InvariantCulture,
                    $"(@scope,{id},@tree,{parent},{left},{right},{depth},{position},1,'fixture')");
            }

            var scope = entity.FindProperty("Scope")!.GetRelationalTypeMapping()
                .CreateParameter(parameterCommand, "scope", NativeFactQueries.Scope, nullable: false);
            var tree = entity.FindProperty("TreeId")!.GetRelationalTypeMapping()
                .CreateParameter(parameterCommand, "tree", NativeFactQueries.Tree, nullable: false);
            await context.Database.ExecuteSqlRawAsync(sql.ToString(), [scope, tree], cancellationToken);
        }

        await using var analyze = context.Database.GetDbConnection().CreateCommand();
        analyze.CommandText = "ANALYZE TABLE " + table;
        await analyze.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Retains duplicate selection facts, duplicate output occurrences and original owner/decoy rows.
    /// </summary>
    private async Task SeedFactsAsync(NativeIndexedContext context, CancellationToken cancellationToken)
    {
        var selected = NativeFactQueries.Selected(Case);

        // WHY: Each source owns four distinct fact identities, including two separately observable occurrences.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < selected.Length; index++)
        {
            var node = selected[index];
            var offset = index * 4;
            context.AddRange(Fact(offset + 1, node, "selected"), Fact(offset + 2, node, "selected"),
                Fact(offset + 3, node, "occurrence"), Fact(offset + 4, node, "occurrence"));
        }

        var end = selected.Length * 4;
        context.AddRange(Fact(end + 1, 99, "unrelated"), Fact(end + 2, 1, "owner"),
            Fact(end + 3, 1, "owner"), Fact(end + 4, selected[0], "owner"));

        if (Case.Operation == "Union")
        {
            context.AddRange(Fact(end + 5, selected[0], "union-extra"), Fact(end + 6, selected[1], "union-extra"));
        }

        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();

        if (Case.Operation is "Any" or "Contains" or "NotAny")
        {
            // WHY: These consumer-owned covering indexes and 100,000 decoys belong to the original subquery case.
            await context.Database.ExecuteSqlRawAsync(
                "CREATE INDEX `IX_ConsumerFacts_Scope_Label_Node` "
                + "ON `AncestorIndexQueryItems` (`Scope`,`Label`,`NodeId`)", cancellationToken);
            await SeedDecoysAsync(context, cancellationToken);
        }

        await context.Database.ExecuteSqlRawAsync("ANALYZE TABLE `AncestorIndexQueryItems`", cancellationToken);
    }

    /// <summary>Adds unrelated predicate rows in bounded batches outside measurement.</summary>
    private static async Task SeedDecoysAsync(NativeIndexedContext context, CancellationToken cancellationToken)
    {
        var scopeProperty = context.Model.FindEntityType(typeof(NativeIndexedFact))!.FindProperty("Scope")!;
        await using var command = context.Database.GetDbConnection().CreateCommand();

        // WHY: Reproduce the original fact-table fan-in independently of measured result cardinality.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var start = 1; start <= 100_000; start += 1_000)
        {
            var sql = new StringBuilder(
                "INSERT INTO `AncestorIndexQueryItems` (`Scope`,`Id`,`NodeId`,`Label`) VALUES ");

            // WHY: Numeric fixture literals have no consumer input; GUID storage retains provider parameters.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var offset = 0; offset < 1_000; offset++)
            {
                if (offset > 0)
                {
                    sql.Append(',');
                }

                sql.Append(CultureInfo.InvariantCulture, $"(@scope,{1_000_000 + start + offset - 1},99,'decoy')");
            }

            var scope = scopeProperty.GetRelationalTypeMapping()
                .CreateParameter(command, "scope", NativeFactQueries.Scope, nullable: false);
            await context.Database.ExecuteSqlRawAsync(sql.ToString(), [scope], cancellationToken);
        }
    }

    /// <summary>Derives all expected ancestor pairs solely from the star/chain adjacency input.</summary>
    private IEnumerable<NativeFactPair> Expected()
    {
        if (!Case.Matches && Case.Operation != "NotAny")
        {
            yield break;
        }

        var selected = NativeFactQueries.Selected(Case);

        // WHY: The source index identifies the two independent occurrence facts used by the original oracle.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < selected.Length; index++)
        {
            var source = selected[index];
            var owner = Case.Inclusive ? source : Case.Chain ? source - 1 : 1;
            var distance = Case.Inclusive ? 0 : 1;

            while (owner >= 1)
            {
                var include = Case.Operation switch
                {
                    "Any" or "Contains" => Case.Matches && owner is 1 or 20,
                    "NotAny" => !Case.Matches || owner is not (1 or 20),
                    _ => true,
                };

                if (include)
                {
                    yield return new NativeFactPair(index * 4 + 3, source, owner, distance);
                    yield return new NativeFactPair(index * 4 + 4, source, owner, distance);
                }

                if (owner == 1)
                {
                    break;
                }

                owner = Case.Chain ? owner - 1 : 1;
                distance++;
            }
        }
    }

    /// <summary>Creates an assigned ordinary application row outside the measured read.</summary>
    private static NativeIndexedFact Fact(int id, int node, string label) => new()
    {
        Scope = NativeFactQueries.Scope,
        Id = id,
        NodeId = node,
        Label = label,
    };
}

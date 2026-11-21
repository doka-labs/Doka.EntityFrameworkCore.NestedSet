namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>
/// Measures actual tracked-identity admission for native and converted keys on an independently reset catalog.
/// </summary>
[Config(typeof(DatabaseBenchmarkConfiguration))]
public class NativeGuardBenchmarks : IDatabaseBenchmark, IAsyncDisposable
{
    private NativeGuardContext? _context;
    private Func<CancellationToken, Task>? _operation;
    private Func<CancellationToken, Task>? _verify;
    private readonly CommandObservation _observation = new();

    /// <summary>Gets or sets a complete former native tracker measurement profile.</summary>
    [ParamsSource(nameof(Cases))]
    public NativeGuardCase Case { get; set; } = new(NativeGuardKeyKind.NativeNumber, 20_000, 1, true);

    /// <summary>
    /// Gets baseline, native and scalar-boundary workloads without discarded samples or timing thresholds.
    /// </summary>
    public static IEnumerable<NativeGuardCase> Cases
    {
        get
        {
            foreach (var kind in new[] { NativeGuardKeyKind.NativeNumber, NativeGuardKeyKind.NativeIdentifier })
            {
                yield return new NativeGuardCase(kind, 1, 1, true);
                yield return new NativeGuardCase(kind, 20_000, 1, true);
                yield return new NativeGuardCase(kind, 20_000, 65, false);
            }

            foreach (var kind in new[] { NativeGuardKeyKind.OrdinalText, NativeGuardKeyKind.ConvertedInteger })
            {
                yield return new NativeGuardCase(kind, 1, 1, false);
                yield return new NativeGuardCase(kind, 65, 1, false);
                yield return new NativeGuardCase(kind, 20_000, 1, false);
                yield return new NativeGuardCase(kind, 769, 65, false);
            }

            yield return new NativeGuardCase(NativeGuardKeyKind.ConvertedInteger, 65, 1, true);
        }
    }

    /// <inheritdoc />
    CommandObservation IDatabaseBenchmark.Observation => _observation;

    /// <summary>Validates the selected shape before any owned native resource is opened.</summary>
    [GlobalSetup]
    public void Initialize()
    {
        ValidateCase(Case);
    }

    /// <summary>Recreates every physical row, registry identity and tracker before the measured invocation.</summary>
    [IterationSetup]
    public void Prepare() => BenchmarkLifecycle.Complete(PrepareAsync(CancellationToken.None));

    /// <summary>Executes exactly one public deletion or coordinated mutation admission.</summary>
    [Benchmark]
    public Task ProtectTrackedIdentities() =>
        (_operation ?? throw new InvalidOperationException("Prepare the native guard first."))(CancellationToken.None);

    /// <summary>Checks retained tracking, registry scope and persisted rows outside measurement.</summary>
    [IterationCleanup]
    public void Verify() => BenchmarkLifecycle.Complete(VerifyAsync(CancellationToken.None));

    /// <summary>Rejects invalid guard geometry without applying unrelated forest benchmark minimums.</summary>
    internal static void ValidateCase(NativeGuardCase value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value.Candidates);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value.Requests);

        if (!Enum.IsDefined(value.Keys) || value.Delete && value.Requests != 1)
        {
            throw new ArgumentException("A native guard deletion requires exactly one requested tree.", nameof(value));
        }
    }

    /// <inheritdoc />
    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        await CleanupAsync();
        Initialize();
        var builder = new DbContextOptionsBuilder();
        BenchmarkEnvironment.ConfigureProvider(
            builder,
            BenchmarkRunOptions.Current.Engine,
            BenchmarkEnvironment.ConnectionString);
        if (BenchmarkRunOptions.Current.Diagnostics)
        {
            builder.AddInterceptors(_observation);
        }

        _context = Case.Keys switch
        {
            NativeGuardKeyKind.NativeNumber => new IntegerGuardContext(builder.Options),
            NativeGuardKeyKind.NativeIdentifier => new GuidGuardContext(builder.Options),
            NativeGuardKeyKind.OrdinalText => new StringGuardContext(builder.Options),
            NativeGuardKeyKind.ConvertedInteger when Case.Delete => new ConvertedTreeGuardContext(builder.Options),
            NativeGuardKeyKind.ConvertedInteger => new ConvertedIntegerGuardContext(builder.Options),
            _ => throw new InvalidOperationException("The selected native key profile is invalid."),
        };
        await _context.Database.EnsureDeletedAsync(cancellationToken);
        if (BenchmarkRunOptions.Current.Engine == BenchmarkEngine.SqliteMemory)
        {
            // WHY: SQLite needs an open owner connection before its in-memory catalog is created.
            await _context.Database.OpenConnectionAsync(cancellationToken);
        }

        await _context.Database.EnsureCreatedAsync(cancellationToken);

        if (BenchmarkRunOptions.Current.Engine != BenchmarkEngine.SqliteMemory)
        {
            await _context.Database.OpenConnectionAsync(cancellationToken);
        }

        switch (Case.Keys)
        {
            case NativeGuardKeyKind.NativeNumber:
                await PrepareTypedAsync<IntegerGuardNode, int, NativeGuardTreeId>(
                    value => new IntegerGuardNode { Id = value },
                    value => new NativeGuardTreeId($"tree-{value:D8}"), cancellationToken);
                break;
            case NativeGuardKeyKind.ConvertedInteger:
                if (Case.Delete)
                {
                    await PrepareTypedAsync<ConvertedTreeGuardNode, int, NativeGuardTreeId>(
                        value => new ConvertedTreeGuardNode { Id = value },
                        value => new NativeGuardTreeId($"tree-{value:D8}"), cancellationToken);
                }
                else
                {
                    await PrepareTypedAsync<ConvertedIntegerGuardNode, int, NativeGuardTreeId>(
                        value => new ConvertedIntegerGuardNode { Id = value },
                        value => new NativeGuardTreeId($"tree-{value:D8}"), cancellationToken);
                }

                break;
            case NativeGuardKeyKind.NativeIdentifier:
                await PrepareTypedAsync<GuidGuardNode, Guid, Guid>(
                    value => new GuidGuardNode { Id = Key(value) }, Key, cancellationToken);
                break;
            case NativeGuardKeyKind.OrdinalText:
                await PrepareTypedAsync<StringGuardNode, string, Guid>(
                    value => new StringGuardNode { Id = $"key-{value:D10}" }, Key, cancellationToken);
                break;
            default:
                throw new InvalidOperationException("The selected native key profile is invalid.");
        }

        _observation.Reset();
    }

    /// <inheritdoc />
    public Task VerifyAsync(CancellationToken cancellationToken) =>
        (_verify ?? throw new InvalidOperationException("Prepare the native guard oracle first."))(cancellationToken);

    /// <inheritdoc />
    [GlobalCleanup]
    public async Task CleanupAsync() => await DisposeAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            _operation = null;
            _verify = null;
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
    /// Shares one typed preparation algorithm without replacing physical key conversion or guard dispatch.
    /// </summary>
    private async Task PrepareTypedAsync<TEntity, TKey, TTreeId>(
        Func<int, TEntity> create,
        Func<int, TTreeId> tree,
        CancellationToken cancellationToken
    )
        where TEntity : class
        where TKey : notnull
        where TTreeId : notnull
    {
        var context = _context!;
        var otherTree = tree(1);
        var wholeTreeTracked = Case.Keys is NativeGuardKeyKind.NativeNumber or NativeGuardKeyKind.NativeIdentifier
            || Case.Delete;
        var children = Case.Candidates - (wholeTreeTracked ? 1 : 0);
        var branches = Enumerable
            .Range(2, children)
            .Select(value => new NestedSetBranch<TEntity>(create(value)))
            .ToArray();
        var requests = Enumerable.Range(2, Case.Requests).Select(tree).ToArray();
        var imports = new List<NestedSetTreeImport<TEntity, TTreeId>>(Case.Requests + 1)
        {
            new(otherTree, new NestedSetBranch<TEntity>(create(1), branches)),
        };

        // WHY: The index supplies disjoint assigned keys for every independently reserved tree.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < requests.Length; index++)
        {
            var rootKey = Case.Candidates + 100 + index * 2;
            var requested = Case.Delete
                ? new NestedSetBranch<TEntity>(create(rootKey), [new NestedSetBranch<TEntity>(create(rootKey + 1))])
                : new NestedSetBranch<TEntity>(create(rootKey));
            imports.Add(new NestedSetTreeImport<TEntity, TTreeId>(requests[index], requested));
        }

        await context.NestedSet<TEntity>().InsertForestAsync(imports, cancellationToken);
        context.ChangeTracker.Clear();
        // WHY: Public read roots are deliberately no-tracking; this workload must prepare actual tracked candidates.
        var candidates = context.NestedSet<TEntity>().InTree(otherTree).Nodes.AsTracking();

        if (!wholeTreeTracked)
        {
            candidates = candidates.Where(node => EF.Property<int>(node, "Depth") > 0);
        }

        var tracked = await candidates.ToArrayAsync(cancellationToken);
        var type = context.Model.FindEntityType(typeof(TEntity))!;
        var locks = requests.Select(identity => new NestedSetTreeLockRequest<TTreeId, NestedSetNoScope>(
            type, default, identity, NestedSetTreeLockMode.Existing)).ToArray();
        var executor = new NestedSetMutationExecutor<TEntity, TKey, TTreeId, NestedSetNoScope>(context, type);
        var admissions = 0;
        _operation = Case.Delete
            ? token => context.NestedSet<TEntity>().DeleteTreeAsync(requests[0], token)
            : token => executor.ExecuteAsync(_ =>
            {
                admissions++;

                return Task.CompletedTask;
            }, locks, token);
        _verify = async token =>
        {
            BenchmarkFixture.Require(Case.Delete || admissions == 1,
                "The native guard did not execute exactly one coordinated callback.");
            BenchmarkFixture.Require(
                tracked.Length == Case.Candidates
                && tracked.All(node => context.Entry(node).State == EntityState.Unchanged),
                "Native guard changed or discarded unrelated tracked candidates.");
            BenchmarkFixture.Require(context.Database.CurrentTransaction is null,
                "Native guard retained its transaction.");
            var remaining = await context.NestedSet<TEntity>().InTree(otherTree).Nodes.CountAsync(token);
            BenchmarkFixture.Require(remaining == children + 1, "Native guard changed unrelated persisted rows.");
            var affected = await context.NestedSet<TEntity>().InTree(requests[0]).Nodes.CountAsync(token);
            BenchmarkFixture.Require(affected == (Case.Delete ? 0 : 1),
                "Native guard did not preserve its requested effect.");
        };
    }

    /// <summary>Supplies disjoint native GUID identities without allocating random input during measurement.</summary>
    private static Guid Key(int value) => new(value, 0, 0, new byte[8]);
}

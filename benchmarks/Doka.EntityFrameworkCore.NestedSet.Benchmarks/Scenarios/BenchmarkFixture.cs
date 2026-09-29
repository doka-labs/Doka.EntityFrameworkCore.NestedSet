namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>
/// Owns one child-process database context and resets every structural and tracked state between iterations.
/// </summary>
internal sealed class BenchmarkFixture : IAsyncDisposable
{
    private readonly BenchmarkRunOptions _options;

    /// <summary>Creates a fixture after its provider configuration has been validated.</summary>
    private BenchmarkFixture(
        BenchmarkContext context,
        BenchmarkScenario scenario,
        BenchmarkRunOptions options,
        CommandObservation counter
    )
    {
        Context = context;
        Scenario = scenario;
        _options = options;
        Counter = counter;
        Forest = BenchmarkForest.Build(scenario);
    }

    /// <summary>Gets the owned context used by the public measured operation.</summary>
    internal BenchmarkContext Context { get; }

    /// <summary>Gets the complete scenario description.</summary>
    internal BenchmarkScenario Scenario { get; }

    /// <summary>Gets the current independently generated import plan.</summary>
    internal BenchmarkForest Forest { get; private set; }

    /// <summary>Gets the untimed command observer; timing runs never register it.</summary>
    internal CommandObservation Counter { get; }

    /// <summary>Gets the scope-bound public hierarchy facade.</summary>
    internal ScopedNestedSet<BenchmarkNode, int> Hierarchy =>
        Context
            .NestedSet<BenchmarkNode>()
            .ForScope(1);

    /// <summary>Gets the last key of the first tree, which is always a leaf in the supported shapes.</summary>
    internal int LeafKey => (Scenario.Nodes / Scenario.Trees) + (Scenario.Nodes % Scenario.Trees > 0 ? 1 : 0);

    /// <summary>Creates a correctly cached manual or configured-order model over an owned provider.</summary>
    /// <param name="scenario">The forest and tracker population to prepare.</param>
    /// <param name="ordered">Whether mapped names determine sibling order.</param>
    /// <param name="options">The provider and diagnostic interception settings.</param>
    /// <param name="connection">The owned database connection string.</param>
    /// <param name="cancellationToken">The token checked before context acquisition.</param>
    /// <returns>The fixture that owns the configured context.</returns>
    internal static BenchmarkFixture Create(
        BenchmarkScenario scenario,
        bool ordered,
        BenchmarkRunOptions options,
        string connection,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        scenario.Validate();
        var counter = new CommandObservation();
        var builder = new DbContextOptionsBuilder().UseNestedSets();

        BenchmarkEnvironment.ConfigureProvider(builder, options.Engine, connection);

        if (options.Diagnostics)
        {
            builder.AddInterceptors(counter);
        }

        BenchmarkContext context = ordered
            ? new OrderedBenchmarkContext(builder.Options)
            : new ManualBenchmarkContext(builder.Options);

        return new BenchmarkFixture(context, scenario, options, counter);
    }

    /// <summary>Recreates the owned catalog so fixed keys and tree identities cannot accumulate tombstones.</summary>
    internal async Task ResetAsync(
        bool seed,
        CancellationToken cancellationToken
    )
    {
        Context.ChangeTracker.Clear();
        await Context.Database.CloseConnectionAsync();
        await Context.Database.EnsureDeletedAsync(cancellationToken);

        if (_options.Engine == BenchmarkEngine.SqliteMemory)
        {
            // WHY: An explicitly open connection retains SQLite's in-memory catalog across setup and measurement.
            await Context.Database.OpenConnectionAsync(cancellationToken);
        }

        await Context.Database.EnsureCreatedAsync(cancellationToken);

        if (_options.Engine != BenchmarkEngine.SqliteMemory)
        {
            await Context.Database.OpenConnectionAsync(cancellationToken);
        }

        Forest = BenchmarkForest.Build(Scenario);

        if (seed)
        {
            await Hierarchy.InsertForestAsync(Forest.Imports, cancellationToken);
        }

        Context.ChangeTracker.Clear();

        for (var index = 0; index < Scenario.Tracked; index++)
        {
            Context.Attach(
                new BenchmarkTrackedEntity
                {
                    Id = index + 1,
                    Payload = new string('t', 1024),
                });
        }

        Counter.Reset();
    }

    /// <summary>Checks every remaining tree and rejects any invalid measured state outside both snapshots.</summary>
    internal async Task ValidateAsync(
        CancellationToken cancellationToken
    )
    {
        var identities = await Context
            .Nodes
            .AsNoTracking()
            .Select(node => node.TreeId)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var identity in identities)
        {
            var report = await Hierarchy
                .InTree(identity)
                .ValidateAsync(NestedSetValidationLevel.Full, cancellationToken);

            Require(report.IsValid, string.Join(", ", report.Issues.Select(issue => issue.Code)));
        }
    }

    /// <summary>
    /// Rejects valid no-ops and incorrect functional results without evaluating performance thresholds.
    /// </summary>
    internal static void Require(
        bool condition,
        string message
    )
    {
        if (!condition)
        {
            throw new InvalidOperationException("Invalid benchmark result: " + message);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Context.DisposeAsync();
}

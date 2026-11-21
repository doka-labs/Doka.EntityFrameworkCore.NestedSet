namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Observes public forest preparation and its complete pre-I/O rejection recovery as one operation.</summary>
/// <remarks>
/// BenchmarkDotNet measures the public operation including deliberate failure recovery. This is not the former
/// thread-local pre-connection counter and its byte totals must not be compared as the same observation boundary.
/// </remarks>
public class ForestPreparationMeasurementBenchmarks : IDisposable
{
    private ForestContext _context = null!;
    private NestedSetBranch<TrackingHierarchy.Node> _root = null!;
    private NestedSetTreeImport<TrackingHierarchy.Node, Guid>[] _imports = null!;
    private ScopedNestedSet<TrackingHierarchy.Node, int> _hierarchy = null!;
    private bool _rejected;

    /// <summary>Releases owned fixture resources independently of semantic verification.</summary>
    public void Dispose()
    {
        try
        {
            Dispose(disposing: true);
        }
        finally
        {
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>Releases managed fixture resources once, including failed or incomplete setup.</summary>
    /// <param name="disposing">Whether this call owns managed-resource cleanup.</param>
    protected virtual void Dispose(
        bool disposing
    )
    {
        if (!disposing)
        {
            return;
        }

        var context = _context;
        _context = null!;
        context?.Dispose();
    }

    /// <summary>Gets or sets the original detached single-tree preparation population.</summary>
    [Params(20_000)]
    public int Nodes { get; set; } = 20_000;

    /// <summary>Creates fresh inputs and services outside every measured public preparation attempt.</summary>
    [IterationSetup]
    public void PrepareIteration()
    {
        _context = new ForestContext();
        _hierarchy = _context.NestedSet<TrackingHierarchy.Node>().ForScope(1);
        var children = Enumerable.Range(2, Nodes - 1)
            .Select(id => new NestedSetBranch<TrackingHierarchy.Node>(new TrackingHierarchy.Node { NodeId = id }))
            .ToArray();
        _root = new NestedSetBranch<TrackingHierarchy.Node>(new TrackingHierarchy.Node { NodeId = 1 }, children);
        _imports = [new NestedSetTreeImport<TrackingHierarchy.Node, Guid>(Guid.Empty, _root)];
        _rejected = false;
    }

    /// <summary>Runs the real public forest path until a deliberate first-connection failure is restored.</summary>
    /// <returns>The benchmark operation, including the public API's protected cleanup.</returns>
    [Benchmark]
    public Task PrepareAndRecoverForest() => PrepareAsync(CancellationToken.None);

    /// <summary>Requires complete restoration and releases the context after the one measured attempt.</summary>
    [IterationCleanup]
    public void CleanupIteration()
    {
        try
        {
            VerifyWorkload();
        }
        finally
        {
            Dispose();
        }
    }

    /// <summary>Requires the complete detached-input, tracker and transaction restoration contract.</summary>
    public void VerifyWorkload()
    {
        if (!_rejected || !_context.Boundary.Reached
            || _context.ChangeTracker.Entries().Any()
            || _context.Database.CurrentTransaction is not null
            || _root.Children.Any(child => child.Entity.Start != 0 || child.Entity.End != 0))
        {
            throw new InvalidOperationException("The public forest preparation workload did not restore its inputs.");
        }
    }

    /// <summary>Distinguishes deliberate pre-I/O failure from an invalid benchmark or a real provider error.</summary>
    private async Task PrepareAsync(
        CancellationToken cancellationToken
    )
    {
        try
        {
            await _hierarchy.InsertForestAsync(_imports, cancellationToken);
        }
        catch (PreparationStoppedException)
        {
            _rejected = true;
        }
    }

    /// <summary>Supplies the same explicitly mapped hierarchy and a test-owned first-I/O boundary.</summary>
    private sealed class ForestContext : TrackingMeasurementContext<ForestContext>
    {
        /// <summary>Gets the operation-owned I/O rejection boundary.</summary>
        internal PreparationBoundary Boundary { get; } = new();

        /// <inheritdoc />
        protected override void OnConfiguring(
            DbContextOptionsBuilder optionsBuilder
        ) => optionsBuilder.AddInterceptors(Boundary);

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => TrackingHierarchy.Configure(modelBuilder);
    }

    /// <summary>Rejects the operation before SQLite can perform physical database I/O.</summary>
    private sealed class PreparationBoundary : DbConnectionInterceptor
    {
        /// <summary>Gets whether public preparation reached the intended connection boundary.</summary>
        internal bool Reached { get; private set; }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            Reached = true;

            return ValueTask.FromException<InterceptionResult>(new PreparationStoppedException());
        }
    }

    /// <summary>Identifies only the intentional pre-I/O failure.</summary>
    private sealed class PreparationStoppedException : Exception;
}

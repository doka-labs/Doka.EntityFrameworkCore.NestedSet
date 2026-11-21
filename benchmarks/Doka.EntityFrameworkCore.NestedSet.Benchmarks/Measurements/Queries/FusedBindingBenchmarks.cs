using Doka.EntityFrameworkCore.NestedSet.Storage;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Observes SQL construction for separate and fused owned bindings on the same real EF mapping.</summary>
public class FusedBindingBenchmarks : IDisposable
{
    private QueryMeasurementModel _model = null!;
    private IQueryable<BenchmarkNode> _separate = null!;
    private IQueryable<BenchmarkNode> _fused = null!;

    /// <summary>Prepares equivalent deferred query shapes outside benchmark invocations.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _model = new QueryMeasurementModel();
        var mapping = _model.Context.Model.FindEntityType(QueryMeasurementModel.ScopedMapping)!;
        var source = _model.Nodes(QueryMeasurementModel.ScopedMapping).AsNoTracking();
        _separate = source
            .Where(NestedSetKeyFilter<BenchmarkNode>.Equal(
                mapping.FindProperty(nameof(BenchmarkNode.Scope))!, 11))
            .Where(NestedSetKeyFilter<BenchmarkNode>.Equal(
                mapping.FindProperty(nameof(BenchmarkNode.TreeId))!, Guid.Empty));

        var tree = _model.Context.NestedSet<BenchmarkNode>(QueryMeasurementModel.ScopedMapping)
            .ForScope(11).InTree(Guid.Empty);

        var ordered = (System.Linq.Expressions.MethodCallExpression)tree.Nodes.Expression;
        _fused = tree.Nodes.Provider.CreateQuery<BenchmarkNode>(ordered.Arguments[0]);
        Validate();
    }

    /// <summary>Constructs SQL for independently retained scope and tree predicates.</summary>
    /// <returns>The provider-generated SQL.</returns>
    [Benchmark(Baseline = true)]
    public string SeparatePredicates() => _separate.ToQueryString();

    /// <summary>Constructs SQL for the current fused owned predicate.</summary>
    /// <returns>The provider-generated SQL.</returns>
    [Benchmark]
    public string FusedPredicate() => _fused.ToQueryString();

    /// <summary>Retains exact SQL equivalence and verifies that construction opened no connection.</summary>
    public void Validate()
    {
        if (SeparatePredicates() != FusedPredicate()
            || _model.Context.Database.GetDbConnection().State != System.Data.ConnectionState.Closed
            || _model.Context.ChangeTracker.Entries().Any())
        {
            throw new InvalidOperationException("Fused binding no longer preserves the disconnected SQL contract.");
        }
    }

    /// <summary>Validates semantics and releases the independently owned metadata graph.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            Validate();
        }
        finally
        {
            Dispose();
        }
    }

    /// <summary>Releases owned setup state independently of semantic cleanup validation.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases managed setup resources when disposal was explicitly requested.</summary>
    /// <param name="disposing">Whether managed state can be released.</param>
    protected virtual void Dispose(
        bool disposing
    )
    {
        if (disposing)
        {
            _model?.Dispose();
        }
    }
}

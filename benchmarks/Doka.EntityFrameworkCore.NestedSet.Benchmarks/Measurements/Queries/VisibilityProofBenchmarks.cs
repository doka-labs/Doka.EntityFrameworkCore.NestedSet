using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Observes visibility proof with one or many references to an existing shared query subtree.</summary>
public class VisibilityProofBenchmarks : IDisposable
{
    private QueryMeasurementModel _model = null!;
    private IQueryable<BenchmarkNode> _source = null!;
    private IEntityType _entity = null!;
    private Expression _bound = null!;
    private readonly Expression<Func<BenchmarkNode, BenchmarkNode>> _selector = node => node;

    /// <summary>Gets or sets how many references reuse the same proof input.</summary>
    [Params(1, 256)]
    public int References { get; set; }

    /// <summary>Constructs the exact shared proof graph without database work.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _model = new QueryMeasurementModel();
        var nodes = _model.Context.NestedSet<BenchmarkNode>(QueryMeasurementModel.ScopedMapping)
            .ForScope(1).InTree(Guid.Empty).Nodes;
        var shared = nodes.Where(node => true).Expression;
        var tests = Enumerable.Range(0, References)
            .Select(_ => (Expression)Expression.Call(typeof(Queryable), nameof(Queryable.Any),
                [typeof(BenchmarkNode)], shared))
            .ToArray();
        var node = Expression.Parameter(typeof(BenchmarkNode), "node");
        _source = nodes.Where(Expression.Lambda<Func<BenchmarkNode, bool>>(tests.Aggregate(Expression.AndAlso), node));
        _entity = _model.Context.Model.FindEntityType(QueryMeasurementModel.ScopedMapping)!;
        _bound = ((MethodCallExpression)nodes.Expression).Arguments[0];
        Validate();
    }

    /// <summary>Establishes one fresh visibility proof over the prepared graph.</summary>
    /// <returns>Whether the exact bound endpoint remains visible.</returns>
    [Benchmark]
    public bool Prove() => NestedSetQueryEndpoint.Require(_source, _selector, _entity, _bound, "node").IsVisible;

    /// <summary>Rejects lost visibility or unintended database work without numeric thresholds.</summary>
    public void Validate()
    {
        if (!Prove() || _model.Context.Database.GetDbConnection().State != System.Data.ConnectionState.Closed)
        {
            throw new InvalidOperationException("Shared visibility no longer preserves its deferred proof.");
        }
    }

    /// <summary>Validates the proof and releases the owned metadata graph.</summary>
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

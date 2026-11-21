using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Observes trusted lexical endpoint resolution for shared source DAGs and named mapping variants.</summary>
public class EndpointDagBenchmarks : IDisposable
{
    private static readonly MethodInfo s_binding = typeof(EndpointDagBenchmarks)
        .GetMethod(nameof(Bind), BindingFlags.NonPublic | BindingFlags.Static)!;
    private QueryMeasurementModel _model = null!;
    private Func<bool> _resolve = null!;

    /// <summary>Gets or sets the source shape retained from endpoint-scaling correctness fixtures.</summary>
    [Params("dag", "identity", "projected", "alternating-same", "alternating-foreign", "object-payload")]
    public string Shape { get; set; } = "dag";

    /// <summary>Gets or sets the number of lexical shared-source layers.</summary>
    [Params(12, 16, 24, 48)]
    public int Depth { get; set; }

    /// <summary>Builds the selected shared query once outside measured operations.</summary>
    [GlobalSetup]
    public void Setup()
    {
        // WHY: Reject an unknown observation before creating provider resources that setup would not return.
        if (Shape is not ("dag" or "identity" or "projected" or "alternating-same"
            or "alternating-foreign" or "object-payload"))
        {
            throw new ArgumentOutOfRangeException(nameof(Shape));
        }

        _model = new QueryMeasurementModel();
        var first = _model.Nodes(QueryMeasurementModel.FirstMapping);
        var mapping = _model.Context.Model.FindEntityType(QueryMeasurementModel.FirstMapping)!;

        switch (Shape)
        {
            case "dag":
                var dag = first;

                // WHY: Each index creates one shared lexical layer; there is no collection to enumerate.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var index = 0; index < Depth; index++)
                {
                    dag = Bind(dag, selected => selected.Concat(selected));
                }

                _resolve = Resolver(dag, node => node, mapping, first.Expression);
                break;
            case "identity":
            case "projected":
                var projected = first.Select(node => new { Node = node });

                // WHY: Each layer must retain the previous source identity rather than enumerate rows.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var index = 0; index < Depth; index++)
                {
                    projected = Shape == "projected"
                        ? Bind(projected, selected => selected.Select(x => new { x.Node })
                            .Concat(selected.Select(y => new { y.Node })))
                        : Bind(projected, selected => selected.Select(x => x).Concat(selected.Select(y => y)));
                }

                _resolve = Resolver(projected, row => row.Node, mapping, first.Expression);
                break;
            case "object-payload":
                var payload = new object();
                var carried = first.Select(node => new { Node = node, Payload = payload });

                // WHY: Indexing constructs the requested depth while preserving one immutable payload.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var index = 0; index < Depth; index++)
                {
                    carried = Bind(carried, selected => selected.Select(x => new { x.Node, x.Payload })
                        .Concat(selected.Select(y => new { y.Node, y.Payload })));
                }

                _resolve = Resolver(carried, row => row.Node, mapping, first.Expression);
                break;
            case "alternating-same":
            case "alternating-foreign":
                var second = Shape == "alternating-foreign"
                    ? _model.Nodes(QueryMeasurementModel.SecondMapping)
                    : first;
                var alternating = first.Join(second, left => left.Id, right => right.Id,
                    (left, right) => new Endpoints { Upper = left, Lower = right });

                // WHY: Endpoint ownership alternates once per lexical layer, independently of query execution.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var index = 0; index < Depth; index++)
                {
                    alternating = Bind(alternating, selected => selected.Select(row => new Endpoints
                    {
                        Upper = row.Lower,
                        Lower = row.Upper,
                    }).Concat(selected.Select(row => row)));
                }

                _resolve = Resolver(alternating, row => row.Upper, mapping, first.Expression,
                    rejectForeign: Shape == "alternating-foreign");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(Shape));
        }

        Validate();
    }

    /// <summary>Resolves one exact selected endpoint or observes the expected foreign-mapping rejection.</summary>
    /// <returns>The validated endpoint's visibility, or false for the explicit rejection control.</returns>
    [Benchmark]
    public bool Resolve() => _resolve();

    /// <summary>Checks the actual mapping outcome without an allocation or timing threshold.</summary>
    public void Validate()
    {
        if (Resolve() != (Shape != "alternating-foreign")
            || _model.Context.Database.GetDbConnection().State != System.Data.ConnectionState.Closed)
        {
            throw new InvalidOperationException("The endpoint fixture changed its mapping or deferred behavior.");
        }
    }

    /// <summary>Validates and disposes owned setup resources after measurement.</summary>
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

    /// <summary>Preserves one typed selector without adding an object box to the measured resolution.</summary>
    private static Func<bool> Resolver<TRow>(
        IQueryable<TRow> source,
        Expression<Func<TRow, BenchmarkNode>> selector,
        IEntityType mapping,
        Expression bound,
        bool rejectForeign = false
    ) => () =>
    {
        try
        {
            var actual = NestedSetQueryEndpoint.Require(source, selector, mapping, bound, "node", s_binding);

            return ReferenceEquals(mapping, actual.EntityType) && actual.IsVisible;
        }
        catch (ArgumentException) when (rejectForeign)
        {
            return false;
        }
    };

    /// <summary>Supplies the trusted statement-local marker without evaluating the composition callback.</summary>
    private static IQueryable<TResult> Bind<TSource, TResult>(
        IQueryable<TSource> source,
        Expression<Func<IQueryable<TSource>, IQueryable<TResult>>> compose
    ) => source.Provider.CreateQuery<TResult>(Expression.Call(
        s_binding.MakeGenericMethod(typeof(TSource), typeof(TResult)), source.Expression, Expression.Quote(compose)));

    /// <summary>Retains two independently named endpoint positions through alternating layers.</summary>
    private sealed class Endpoints
    {
        /// <summary>Gets or sets the upper selected endpoint.</summary>
        public required BenchmarkNode Upper { get; init; }

        /// <summary>Gets or sets the lower selected endpoint.</summary>
        public required BenchmarkNode Lower { get; init; }
    }
}

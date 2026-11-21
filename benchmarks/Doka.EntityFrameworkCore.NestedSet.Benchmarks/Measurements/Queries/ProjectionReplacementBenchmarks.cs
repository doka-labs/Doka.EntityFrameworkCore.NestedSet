using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Observes direct replacement beside the retained synthetic-lambda substitution path.</summary>
public class ProjectionReplacementBenchmarks
{
    private Expression<Func<int, InliningMeasurementInput.Projection>> _projection = null!;
    private Expression<Func<InliningMeasurementInput.Projection, int>> _selector = null!;

    /// <summary>Builds independent projection and selector inputs outside measured invocations.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _projection = value => new InliningMeasurementInput.Projection { First = value };
        _selector = row => row.First;
        Validate();
    }

    /// <summary>Builds the former intermediate lambda before the actual substitution.</summary>
    /// <returns>The selected source argument.</returns>
    [Benchmark(Baseline = true)]
    public Expression SyntheticLambda() => NestedSetQueryExpressions.Inline(
        Expression.Lambda(_selector.Body, _selector.Parameters[0]), _projection.Body);

    /// <summary>Replaces the one parameter without building an intermediate lambda.</summary>
    /// <returns>The selected source argument.</returns>
    [Benchmark]
    public Expression Replace() => NestedSetQueryExpressions.Replace(
        _selector.Body, _selector.Parameters[0], _projection.Body);

    /// <summary>Rejects differences in the selected lexical argument independently of observed values.</summary>
    [GlobalCleanup]
    public void Validate()
    {
        if (!ReferenceEquals(_projection.Parameters[0], SyntheticLambda())
            || !ReferenceEquals(_projection.Parameters[0], Replace()))
        {
            throw new InvalidOperationException("Projection replacement changed its lexical source.");
        }
    }
}

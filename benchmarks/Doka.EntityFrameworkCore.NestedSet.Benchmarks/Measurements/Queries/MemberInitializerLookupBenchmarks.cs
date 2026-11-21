using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Observes selection from a prepared initializer without visiting its unrelated assignments.</summary>
public class MemberInitializerLookupBenchmarks
{
    private Expression<Func<InliningMeasurementInput.Projection, int>> _selector = null!;
    private MemberInitExpression _projection = null!;
    private Expression _expected = null!;

    /// <summary>Builds the actual three-assignment input outside measured invocations.</summary>
    [GlobalSetup]
    public void Setup()
    {
        Expression<Func<InliningMeasurementInput.Projection>> projection = () => new InliningMeasurementInput.Projection
        {
            First = 11,
            Ignored = 17,
            Last = 29,
        };

        _projection = (MemberInitExpression)projection.Body;
        _selector = row => row.Last;
        _expected = ((MemberAssignment)_projection.Bindings[2]).Expression;
        Validate();
    }

    /// <summary>Reads one prepared member through the actual query-expression implementation.</summary>
    /// <returns>The selected existing assignment.</returns>
    [Benchmark]
    public Expression SelectMember() => NestedSetQueryExpressions.Inline(_selector, _projection);

    /// <summary>Checks exact assignment identity without a performance or allocation threshold.</summary>
    [GlobalCleanup]
    public void Validate()
    {
        if (!ReferenceEquals(_expected, SelectMember()))
        {
            throw new InvalidOperationException("Member initialization selection changed.");
        }
    }
}

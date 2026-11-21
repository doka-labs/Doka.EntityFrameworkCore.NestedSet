using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Observes fixed and general expression bindings without numeric acceptance thresholds.</summary>
public class FixedBindingBenchmarks
{
    private InliningMeasurementInput _input = null!;

    /// <summary>Gets or sets the number of fixed bindings.</summary>
    [Params(1, 2, 3)]
    public int Arity { get; set; }

    /// <summary>Gets or sets whether direct member selection replaces complete-argument selection.</summary>
    [Params(false, true)]
    public bool Member { get; set; }

    /// <summary>Prepares exact expression identities outside measured invocations.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _input = new InliningMeasurementInput(Arity, Member);
        Validate();
    }

    /// <summary>Invokes the retained general path, including its call-owned explicit argument array.</summary>
    /// <returns>The selected expression.</returns>
    [Benchmark(Baseline = true)]
    public Expression GeneralArray() => Arity switch
    {
        1 => NestedSetQueryExpressions.Inline(_input.Lambda, new Expression[] { _input.Arguments[0] }),
        2 => NestedSetQueryExpressions.Inline(_input.Lambda,
            new Expression[] { _input.Arguments[0], _input.Arguments[1] }),
        3 => NestedSetQueryExpressions.Inline(_input.Lambda,
            new Expression[] { _input.Arguments[0], _input.Arguments[1], _input.Arguments[2] }),
        _ => throw new InvalidOperationException("Only fixed overload arities are configured."),
    };

    /// <summary>Invokes one fixed overload using the original argument objects.</summary>
    /// <returns>The selected expression.</returns>
    [Benchmark]
    public Expression Fixed() => _input.Inline();

    /// <summary>Rejects changed substitution semantics independently of measurement values.</summary>
    [GlobalCleanup]
    public void Validate()
    {
        if (!ReferenceEquals(_input.Expected, Fixed()) || !ReferenceEquals(_input.Expected, GeneralArray()))
        {
            throw new InvalidOperationException("Expression bindings no longer retain the supplied identity.");
        }
    }
}

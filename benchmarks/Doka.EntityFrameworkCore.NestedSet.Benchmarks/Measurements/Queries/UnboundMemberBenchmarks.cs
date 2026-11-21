using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Observes direct unbound member reuse without evaluating captured or static getters.</summary>
public class UnboundMemberBenchmarks
{
    private InliningMeasurementInput _input = null!;

    /// <summary>Gets or sets the fixed binding count.</summary>
    [Params(1, 2, 3)]
    public int Arity { get; set; }

    /// <summary>Gets or sets whether the unbound member is static.</summary>
    [Params(false, true)]
    public bool StaticMember { get; set; }

    /// <summary>Prepares an application getter that throws if expression construction evaluates it.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _input = new InliningMeasurementInput(Arity, false, unbound: true, staticMember: StaticMember);
        Validate();
    }

    /// <summary>Substitutes one fixed binding while retaining the unbound member expression.</summary>
    /// <returns>The unchanged member expression.</returns>
    [Benchmark]
    public Expression Inline() => _input.Inline();

    /// <summary>Rejects identity changes and accidental getter execution without numeric budgets.</summary>
    [GlobalCleanup]
    public void Validate()
    {
        if (!ReferenceEquals(_input.Expected, Inline()))
        {
            throw new InvalidOperationException("An unbound expression was replaced.");
        }
    }
}

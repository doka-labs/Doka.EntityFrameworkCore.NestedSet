using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Owns immutable prepared inputs shared by independent expression-inlining observations.</summary>
internal sealed class InliningMeasurementInput
{
    private readonly InvalidOperationException _getterFailure = new("Query construction read an application getter.");

    /// <summary>Constructs the selected fixed-arity input without executing an application getter.</summary>
    internal InliningMeasurementInput(
        int arity,
        bool member,
        bool unbound = false,
        bool staticMember = false
    )
    {
        // WHY: This workload compares the three fixed overloads; other arities have no matching observation.
        if (arity is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(arity));
        }

        var parameters = Enumerable.Range(0, arity)
            .Select(_ => Expression.Parameter(member ? typeof(Projection) : typeof(int), "row"))
            .ToArray();

        var value = Expression.Constant(29);
        Expression argument = member
            ? Expression.MemberInit(Expression.New(typeof(Projection)),
                Expression.Bind(typeof(Projection).GetProperty(nameof(Projection.Last))!, value))
            : value;

        Expression body = member
            ? Expression.Property(parameters[^1], nameof(Projection.Last))
            : parameters[^1];

        if (unbound)
        {
            body = staticMember
                ? Expression.Property(null, typeof(InliningMeasurementInput).GetProperty(
                    nameof(UnreadStaticValue), BindingFlags.Static | BindingFlags.NonPublic)!)
                : Expression.Property(Expression.Constant(this), nameof(UnreadValue));
        }

        Lambda = Expression.Lambda(body, parameters);
        Arguments = Enumerable.Repeat(argument, arity).ToArray();
        Expected = unbound ? body : value;
    }

    /// <summary>Gets the prepared lambda with independent lexical parameter identities.</summary>
    internal LambdaExpression Lambda { get; }

    /// <summary>Gets the fixed argument objects, also used by the general overload.</summary>
    internal Expression[] Arguments { get; }

    /// <summary>Gets the exact expression identity each measured path must retain.</summary>
    internal Expression Expected { get; }

    /// <summary>Throws if construction unexpectedly reads an ordinary captured application member.</summary>
    public int UnreadValue => throw _getterFailure;

    private static int UnreadStaticValue => throw new InvalidOperationException("A static getter was evaluated.");

    /// <summary>Executes one fixed-arity substitution over the retained inputs.</summary>
    internal Expression Inline() => Arguments.Length switch
    {
        1 => NestedSetQueryExpressions.Inline(Lambda, Arguments[0]),
        2 => NestedSetQueryExpressions.Inline(Lambda, Arguments[0], Arguments[1]),
        3 => NestedSetQueryExpressions.Inline(Lambda, Arguments[0], Arguments[1], Arguments[2]),
        _ => throw new InvalidOperationException("Only the three fixed binding overloads are observed."),
    };

    /// <summary>Supplies independent initializer members without hierarchy or storage state.</summary>
    internal sealed class Projection
    {
        /// <summary>Gets or sets the first initialized member.</summary>
        public int First { get; set; }

        /// <summary>Gets or sets the unrelated initializer member.</summary>
        public int Ignored { get; set; }

        /// <summary>Gets or sets the selected initialized member.</summary>
        public int Last { get; set; }
    }
}

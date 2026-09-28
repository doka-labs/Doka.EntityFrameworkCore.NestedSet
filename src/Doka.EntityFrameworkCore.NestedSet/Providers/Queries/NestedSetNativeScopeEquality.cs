namespace Doka.EntityFrameworkCore.NestedSet.Providers;

/// <summary>Retains a known Scope type until native relational comparison translation.</summary>
/// <typeparam name="TScope">The exact mapped Scope CLR type, including converted identities.</typeparam>
internal static class NestedSetNativeScopeEquality<TScope>
    where TScope : notnull
{
    private static readonly MethodInfo s_equal = ResolveMethod();

    /// <summary>Builds a translated comparison without repeated generic reflection or erased Scope values.</summary>
    /// <param name="left">The mapped Scope column expression.</param>
    /// <param name="right">The captured typed candidate Scope expression.</param>
    /// <returns>The native equality marker translated by the relational plugin.</returns>
    internal static MethodCallExpression Match(
        Expression left,
        Expression right
    ) => Expression.Call(s_equal, left, right);

    /// <summary>Marks a native equality operation that must never execute as a CLR comparison.</summary>
    /// <param name="left">The mapped Scope column operand.</param>
    /// <param name="right">The typed candidate Scope operand.</param>
    /// <returns>The database-native comparison result after SQL translation.</returns>
    internal static bool Equal(
        TScope left,
        TScope right
    ) => throw new InvalidOperationException("Native Scope equality is supported only in translated queries.");

    /// <summary>Resolves the exact closed marker method once through a strongly typed expression.</summary>
    private static MethodInfo ResolveMethod()
    {
        Expression<Func<TScope, TScope, bool>> comparison = (left, right) => Equal(left, right);

        return ((MethodCallExpression)comparison.Body).Method;
    }
}

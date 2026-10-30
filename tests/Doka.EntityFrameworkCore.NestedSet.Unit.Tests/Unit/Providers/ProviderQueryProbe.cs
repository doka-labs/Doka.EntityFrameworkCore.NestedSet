using System.Collections;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using ExpressionFactory = System.Linq.Expressions.Expression;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Observes the real typed provider operators while rejecting every attempted execution.</summary>
internal sealed class ProviderQueryProbe : IAsyncQueryProvider
{
    /// <summary>Gets the number of unexpected synchronous or asynchronous execution attempts.</summary>
    internal int Executions { get; private set; }

    /// <summary>Gets an exact construction failure used to detect exception wrapping at the typed boundary.</summary>
    internal Exception? ConstructionError { get; init; }

    /// <summary>Creates an unexecuted root whose expression remains owned by this probe.</summary>
    internal IQueryable<T> Source<T>() => new Query<T>(this);

    /// <inheritdoc />
    public IQueryable CreateQuery(Expression expression)
        => throw new InvalidOperationException("The typed provider contract must retain generic query construction.");

    /// <inheritdoc />
    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
    {
        if (ConstructionError is { } error)
        {
            throw error;
        }

        return new Query<TElement>(this, expression);
    }

    /// <inheritdoc />
    public object? Execute(Expression expression) => throw Execution();

    /// <inheritdoc />
    public TResult Execute<TResult>(Expression expression) => throw Execution();

    /// <inheritdoc />
    public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
        => throw Execution();

    /// <summary>Records a boundary crossing rather than returning invented provider results.</summary>
    private InvalidOperationException Execution()
    {
        Executions++;

        return new InvalidOperationException("Query construction must not execute a provider operation.");
    }

    /// <summary>Retains a real operator expression without providing an execution fallback.</summary>
    private sealed class Query<T> : IOrderedQueryable<T>
    {
        /// <summary>Creates the probe's initial root.</summary>
        internal Query(ProviderQueryProbe provider)
        {
            Provider = provider;
            Expression = ExpressionFactory.Constant(this);
        }

        /// <summary>Retains the exact expression produced by a public query operator.</summary>
        internal Query(ProviderQueryProbe provider, Expression expression)
        {
            Provider = provider;
            Expression = expression;
        }

        /// <inheritdoc />
        public Type ElementType => typeof(T);

        /// <inheritdoc />
        public Expression Expression { get; }

        /// <inheritdoc />
        public IQueryProvider Provider { get; }

        /// <inheritdoc />
        public IEnumerator<T> GetEnumerator() => throw ((ProviderQueryProbe)Provider).Execution();

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

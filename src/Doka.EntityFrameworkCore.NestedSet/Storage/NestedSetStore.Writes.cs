namespace Doka.EntityFrameworkCore.NestedSet.Storage;

internal sealed partial class NestedSetStore<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    /// <summary>Sets parent keys through an EF setter closed over the actual nullable key type.</summary>
    /// <remarks>
    ///     EF requires the property's real CLR type when translating an update. Closing the delegate once per
    ///     store type supports nullable value keys and reference keys without internal EF APIs or boxed selectors.
    /// </remarks>
    private static readonly Func<IQueryable<TEntity>, string, NestedSetParent<TKey>, CancellationToken, Task<int>>
        s_setParent = typeof(NestedSetStore<TEntity, TKey, TTreeId, TScope>).GetMethod(
                nameof(SetParentTypedAsync),
                BindingFlags.NonPublic | BindingFlags.Static)!
            // WHY: EF can require a nullable CLR key; its parent reuses that nullable type instead of nesting it.
            .MakeGenericMethod(
                typeof(TKey).IsValueType && Nullable.GetUnderlyingType(typeof(TKey)) is null
                    ? typeof(Nullable<>).MakeGenericType(typeof(TKey))
                    : typeof(TKey))
            .CreateDelegate<Func<IQueryable<TEntity>, string, NestedSetParent<TKey>, CancellationToken, Task<int>>>();

    /// <summary>Creates the typed EF property access needed by the cached parent setter delegate.</summary>
    /// <typeparam name="TValue">The nullable parent property's exact CLR type.</typeparam>
    /// <param name="name">The mapped parent property name.</param>
    /// <returns>A typed selector suitable for the generic EF update setter.</returns>
    private static Expression<Func<TEntity, TValue>> CreateProperty<TValue>(
        string name
    )
    {
        var parameter = Expression.Parameter(typeof(TEntity), "node");

        return Expression.Lambda<Func<TEntity, TValue>>(
            NestedSetExpressions.Property(parameter, name, typeof(TValue)),
            parameter);
    }

    /// <summary>Applies an affine transformation to one 64-bit coordinate entirely in the database.</summary>
    /// <param name="query">The scoped rows to update.</param>
    /// <param name="name">The mapped 64-bit coordinate property name.</param>
    /// <param name="multiplier">The factor applied before the offset, including minus one for staged bounds.</param>
    /// <param name="offset">The amount added to each multiplied value.</param>
    /// <param name="cancellationToken">The token used to cancel the update.</param>
    /// <returns>The number of affected rows.</returns>
    /// <remarks>The caller must hold the write lock and validate every checked endpoint before the update.</remarks>
    internal Task<int> ChangeLongAsync(
        IQueryable<TEntity> query,
        string name,
        long multiplier,
        long offset,
        CancellationToken cancellationToken
    )
    {
        if (multiplier == 1
            && offset == 0)
        {
            // WHY: A no-op assignment can still create row versions or fire update triggers; skipping it avoids both.
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(0);
        }

        var property = Property<long>(name);
        var value = Expression.Lambda<Func<TEntity, long>>(
            Expression.Add(Expression.Multiply(property.Body, Capture(multiplier)), Capture(offset)),
            property.Parameters);

        return NestedSetTelemetry.TrackRows(
            query.ExecuteUpdateAsync(setters => setters.SetProperty(property, value),cancellationToken));
    }

    /// <summary>Assigns one 64-bit coordinate to the same value across a scoped query.</summary>
    /// <param name="query">The rows selected while holding the write lock.</param>
    /// <param name="name">The mapped 64-bit coordinate property name.</param>
    /// <param name="value">The replacement value.</param>
    /// <param name="cancellationToken">The token used to cancel the update.</param>
    /// <returns>The number of affected rows.</returns>
    internal Task<int> SetLongAsync(
        IQueryable<TEntity> query,
        string name,
        long value,
        CancellationToken cancellationToken
    ) => NestedSetTelemetry.TrackRows(
        query.ExecuteUpdateAsync(setters => setters.SetProperty(Property<long>(name), value), cancellationToken));

    /// <summary>Changes the parent of every selected node using the configured nullable key type.</summary>
    /// <param name="query">The rows selected while holding the write lock.</param>
    /// <param name="value">The typed parent, or an absent value to make selected nodes roots.</param>
    /// <param name="cancellationToken">The token used to cancel the update.</param>
    /// <returns>The number of affected rows.</returns>
    internal Task<int> SetParentAsync(
        IQueryable<TEntity> query,
        NestedSetParent<TKey> value,
        CancellationToken cancellationToken
    ) => NestedSetTelemetry.TrackRows(s_setParent(query, Map.Parent, value, cancellationToken));

    /// <summary>Provides the strongly typed parent setter used by the cached reflection-created delegate.</summary>
    /// <typeparam name="TParent">The nullable CLR type of the mapped parent property.</typeparam>
    /// <param name="query">The rows selected while holding the write lock.</param>
    /// <param name="name">The mapped parent property name.</param>
    /// <param name="value">The typed parent identity, or an absent value for roots.</param>
    /// <param name="cancellationToken">The token used to cancel the update.</param>
    /// <returns>The number of affected rows.</returns>
    private static Task<int> SetParentTypedAsync<TParent>(
        IQueryable<TEntity> query,
        string name,
        NestedSetParent<TKey> value,
        CancellationToken cancellationToken
    )
    {
        var mappedParent = ParentValue<TParent>.Convert(value);

        return query.ExecuteUpdateAsync(
            setters => setters.SetProperty(CreateProperty<TParent>(name), mappedParent),
            cancellationToken);
    }

    /// <summary>Converts typed optional keys to the actual nullable CLR type once per closed setter type.</summary>
    private static class ParentValue<TParent>
    {
        internal static readonly Func<NestedSetParent<TKey>, TParent> Convert = Create();

        /// <summary>Compiles an unboxed nullable-value or nullable-reference adaptation for the EF setter.</summary>
        private static Func<NestedSetParent<TKey>, TParent> Create()
        {
            var parent = Expression.Parameter(typeof(NestedSetParent<TKey>), "parent");
            var present = Expression.Property(parent, nameof(NestedSetParent<TKey>.HasValue));
            var value = Expression.Property(parent, nameof(NestedSetParent<TKey>.Value));
            var nullable = Expression.Condition(
                present,
                Expression.Convert(value, typeof(TParent)),
                Expression.Default(typeof(TParent)));

            // WHY: TKey? is not Nullable<TKey> for this generic class. The cached delegate constructs the actual
            // mapped nullable type without boxing each parent key or using reflection for each update.
            return Expression
                .Lambda<Func<NestedSetParent<TKey>, TParent>>(nullable, parent)
                .Compile();
        }
    }

    /// <summary>Opens or closes an interval by shifting each qualifying boundary independently.</summary>
    /// <param name="position">The inclusive boundary at which the shift starts.</param>
    /// <param name="delta">The signed interval width to insert or remove.</param>
    /// <param name="cancellationToken">The token used to cancel the updates.</param>
    /// <returns>A task that completes after both boundary columns have been updated in one row pass.</returns>
    /// <remarks>The caller holds the write lock and validates checked destination endpoints before this call.</remarks>
    internal async Task ShiftBoundsAsync(
        long position,
        long delta,
        CancellationToken cancellationToken
    )
    {
        if (delta == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return;
        }

        // WHY: Every qualifying left boundary implies a qualifying right boundary in a valid interval. Each setter
        // reads only its own old column, so MySQL's left-to-right assignment semantics cannot change either result.
        await NestedSetTelemetry
            .TrackRows(
                Nodes
                    .Where(node => EF.Property<long>(node, Map.Right) >= position)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(
                                Property<long>(Map.Left),
                                node => EF.Property<long>(node, Map.Left) >= position
                                    ? EF.Property<long>(node, Map.Left) + delta
                                    : EF.Property<long>(node, Map.Left))
                            .SetProperty(Property<long>(Map.Right), node => EF.Property<long>(node, Map.Right) + delta),
                        cancellationToken))
            .ConfigureAwait(false);
    }
}

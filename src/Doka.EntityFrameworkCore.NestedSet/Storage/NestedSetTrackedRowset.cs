namespace Doka.EntityFrameworkCore.NestedSet.Storage;

/// <summary>Correlates bounded caller identities in SQL without assuming CLR and database equality agree.</summary>
/// <typeparam name="TEntity">The mapped entity supplying native property conversions and collations.</typeparam>
internal static class NestedSetTrackedRowset<TEntity>
    where TEntity : class
{
    /// <summary>Creates one bounded UNION ALL query whose rows retain the exact requested identity ordinal.</summary>
    /// <typeparam name="TValue">The mapped identity or scope type.</typeparam>
    /// <param name="source">
    /// The mapped query; the consumer must retain the operation's authoritative database scope.
    /// </param>
    /// <param name="property">The mapped property compared using database equality.</param>
    /// <param name="values">At most one batch of input values, including database-equal CLR aliases.</param>
    /// <returns>A composable query that projects only the structure selected by its consumer.</returns>
    internal static IQueryable<Row> Match<TValue>(
        IQueryable<TEntity> source,
        string property,
        IReadOnlyList<TValue> values
    ) => values.Count is 0 or > NestedSetBatch.MaximumRows
        ? throw new ArgumentOutOfRangeException(nameof(values))
        : Union(values.Count, index => source.Where(NestedSetKeyFilter<TEntity>.Equal(property, values[index])));

    /// <summary>Creates one bounded UNION ALL query whose branches match both parts of a scoped identity.</summary>
    /// <typeparam name="TScope">The mapped scope type.</typeparam>
    /// <typeparam name="TValue">The mapped identity type.</typeparam>
    /// <param name="source">The mapped query; the consumer must retain the operation's authoritative filters.</param>
    /// <param name="scopeProperty">The mapped scope property compared using database equality.</param>
    /// <param name="property">The mapped identity property compared using database equality.</param>
    /// <param name="scopes">The scope of each requested identity, aligned with <paramref name="values" />.</param>
    /// <param name="values">At most one batch of identities, which may repeat in other scopes.</param>
    /// <returns>A composable query whose ordinals refer to the aligned input pairs.</returns>
    internal static IQueryable<Row> MatchScoped<TScope, TValue>(
        IQueryable<TEntity> source,
        string scopeProperty,
        string property,
        IReadOnlyList<TScope> scopes,
        IReadOnlyList<TValue> values
    )
    {
        if (values.Count is 0 or > NestedSetBatch.MaximumRows
            || scopes.Count != values.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(values));
        }

        return Union(
            values.Count,
            index => source
                .Where(NestedSetKeyFilter<TEntity>.Equal(scopeProperty, scopes[index]))
                .Where(NestedSetKeyFilter<TEntity>.Equal(property, values[index])));
    }

    /// <summary>Finds database-equal scopes once per distinct input instead of once per tracked entity.</summary>
    /// <typeparam name="TValue">The mapped scope type.</typeparam>
    /// <typeparam name="TKey">The mapped node key type.</typeparam>
    /// <param name="source">The hierarchy restricted to the affected persisted scopes.</param>
    /// <param name="property">The immutable mapped scope property.</param>
    /// <param name="keyProperty">The mapped node key used for deterministic, bounded probes.</param>
    /// <param name="values">Distinct CLR scope values whose equality is resolved by SQL.</param>
    /// <returns>Matching input ordinals with at most one row per input scope.</returns>
    internal static IQueryable<int> ExistingScopes<TValue, TKey>(
        IQueryable<TEntity> source,
        string property,
        string keyProperty,
        IReadOnlyList<TValue> values
    )
    {
        if (values.Count is 0 or > NestedSetBatch.MaximumRows)
        {
            throw new ArgumentOutOfRangeException(nameof(values));
        }

        IQueryable<int>? query = null;
        for (var index = 0; index < values.Count; index++)
        {
            var ordinal = index;
            // WHY: The scope/key index can satisfy an ordered existence probe without scanning every
            // matching node, while the ordering keeps Take deterministic and warning-free.
            var branch = source
                .Where(NestedSetKeyFilter<TEntity>.Equal(property, values[index]))
                .OrderBy(node => EF.Property<TKey>(node, keyProperty))
                .Select(_ => ordinal)
                .Take(1);

            query = query is null ? branch : query.Concat(branch);
        }

        return query!;
    }

    /// <summary>Concatenates one ordinal-tagged branch per requested identity.</summary>
    private static IQueryable<Row> Union(
        int count,
        Func<int, IQueryable<TEntity>> branch
    )
    {
        IQueryable<Row>? query = null;
        for (var index = 0; index < count; index++)
        {
            var ordinal = index;
            var rows = branch(index)
                .Select(entity => new Row
                {
                    Entity = entity,
                    Ordinal = ordinal
                });

            // WHY: The ordinal remains in SQL, so aliases and converted keys never need a CLR lookup fallback.
            query = query is null ? rows : query.Concat(rows);
        }

        return query!;
    }

    /// <summary>Carries a server-side entity projection until the final scalar-only selector is applied.</summary>
    internal sealed class Row
    {
        /// <summary>Gets the original input index, including for noncanonical key aliases.</summary>
        public int Ordinal { get; init; }

        /// <summary>Gets the mapped entity expression; consumers project scalars before materialization.</summary>
        public TEntity Entity { get; init; } = null!;
    }
}

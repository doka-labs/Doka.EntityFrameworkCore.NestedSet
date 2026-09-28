namespace Doka.EntityFrameworkCore.NestedSet.Storage;

/// <summary>Builds bounded equality predicates using mapped key values and native database comparisons.</summary>
/// <typeparam name="TEntity">The mapped hierarchy entity.</typeparam>
internal static class NestedSetKeyFilter<TEntity>
    where TEntity : class
{
    /// <summary>
    /// Builds bounded native membership or scalar equality using the actual finalized property mapping.
    /// </summary>
    /// <typeparam name="TValue">The exact mapped key or scope type, including binary keys.</typeparam>
    /// <param name="property">The mapped immutable key or scope property.</param>
    /// <param name="values">The bounded values compared in the database.</param>
    /// <returns>A parameterized collection predicate or a balanced scalar-equality fallback.</returns>
    internal static Expression<Func<TEntity, bool>> Matches<TValue>(
        IProperty property,
        IReadOnlyList<TValue> values
    )
    {
        var parameter = Expression.Parameter(typeof(TEntity), "node");

        return Expression.Lambda<Func<TEntity, bool>>(Matches(parameter, property, values), parameter);
    }

    /// <summary>Builds the same metadata-aware predicate over an existing entity expression.</summary>
    /// <typeparam name="TValue">The exact mapped key or scope type, including binary keys.</typeparam>
    /// <param name="entity">The entity expression owned by the surrounding lambda.</param>
    /// <param name="property">The mapped immutable key or scope property.</param>
    /// <param name="values">The bounded values compared in the database.</param>
    /// <returns>A predicate body that shares the caller's parameter.</returns>
    internal static Expression Matches<TValue>(
        Expression entity,
        IProperty property,
        IReadOnlyList<TValue> values
    )
    {
        RequireMappedType<TValue>(property);
        var access = NestedSetExpressions.Property(entity, property);

        if (values.Count > 1
            && SupportsCollection<TValue>(property))
        {
            // WHY: Structural batches require separate SQL parameters even when application queries prefer
            // constants or one packed collection. Copy once so callers cannot replace captured keys later.
            var batch = values.ToArray();
            Expression<Func<TValue[]>> captured = () => EF.MultipleParameters(batch);

            return Expression.Call(
                typeof(Enumerable),
                nameof(Enumerable.Contains),
                [typeof(TValue)],
                captured.Body,
                access);
        }

        return Combine(0, values.Count);

        Expression Combine(
            int start,
            int count
        )
        {
            if (count == 0)
            {
                return Expression.Constant(false);
            }

            if (count == 1)
            {
                return EqualValue(access, values[start]);
            }

            var half = count / 2;

            return Expression.OrElse(Combine(start, half), Combine(start + half, count - half));
        }
    }

    /// <summary>Builds one scalar comparison without allocating a singleton collection.</summary>
    /// <typeparam name="TValue">The mapped scalar value type.</typeparam>
    /// <param name="property">The exact mapped scalar property name.</param>
    /// <param name="value">The captured scalar value compared in the database.</param>
    /// <returns>A parameterized scalar equality predicate.</returns>
    internal static Expression<Func<TEntity, bool>> Equal<TValue>(
        string property,
        TValue value
    )
    {
        var parameter = Expression.Parameter(typeof(TEntity), "node");

        return Expression.Lambda<Func<TEntity, bool>>(Equal(parameter, property, value), parameter);
    }

    /// <summary>Builds scalar equality sharing the surrounding lambda's entity expression.</summary>
    /// <typeparam name="TValue">The mapped scalar value type.</typeparam>
    /// <param name="entity">The caller-owned entity expression.</param>
    /// <param name="property">The exact mapped scalar property name.</param>
    /// <param name="value">The captured scalar value compared in the database.</param>
    /// <returns>A parameterized scalar comparison body.</returns>
    internal static Expression Equal<TValue>(
        Expression entity,
        string property,
        TValue value
    ) => EqualValue(NestedSetExpressions.Property(entity, property, typeof(TValue)), value);

    /// <summary>
    /// Checks collection eligibility against the finalized converter and qualified native type matrix.
    /// </summary>
    /// <typeparam name="TValue">The exact mapped scalar type.</typeparam>
    /// <param name="property">The mapped key or scope property.</param>
    /// <returns>Whether a bounded scalar collection is qualified for this actual mapping.</returns>
    internal static bool SupportsCollection<TValue>(
        IProperty property
    )
    {
        RequireMappedType<TValue>(property);

        // WHY: Native CLR types can still be stored through converters. Their scalar equality remains supported,
        // but converted collections are outside the qualified native matrix and consistently use scalar OR.
        // PostgreSQL int[] is a native array key; byte[] is a qualified scalar binary database value.
        return property.GetTypeMapping()
                .Converter is null
            && (typeof(TValue) == typeof(int)
                || typeof(TValue) == typeof(long)
                || typeof(TValue) == typeof(short)
                || typeof(TValue) == typeof(byte)
                || typeof(TValue) == typeof(bool)
                || typeof(TValue) == typeof(decimal)
                || typeof(TValue) == typeof(float)
                || typeof(TValue) == typeof(double)
                || typeof(TValue) == typeof(Guid)
                || typeof(TValue) == typeof(string)
                || typeof(TValue) == typeof(byte[])
                || typeof(TValue) == typeof(DateTime)
                || typeof(TValue) == typeof(DateTimeOffset)
                || typeof(TValue) == typeof(DateOnly)
                || typeof(TValue) == typeof(TimeOnly)
                || typeof(TValue) == typeof(TimeSpan));
    }

    /// <summary>Captures one value while preserving operator-free converted struct equality.</summary>
    private static Expression EqualValue<TValue>(
        Expression access,
        TValue value
    )
    {
        Expression<Func<TValue>> captured = () => value;

        // WHY: Converted structs can implement Equals(TValue) without operator ==. The typed equality primitive
        // preserves their scalar SQL translation and also serves singleton rowsets without a collection policy.
        return NestedSetTypedEquality<TValue>.Equal(access, captured.Body);
    }

    /// <summary>Rejects a generic scalar type that does not match its exact mapped property.</summary>
    private static void RequireMappedType<TValue>(
        IProperty property
    )
    {
        if (property.ClrType != typeof(TValue))
        {
            throw new ArgumentException(
                "The predicate value type must match the mapped property type.",
                nameof(property));
        }
    }
}

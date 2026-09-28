namespace Doka.EntityFrameworkCore.NestedSet.Features.Insert;

/// <summary>Refreshes generated concurrency values on a node that will be returned detached after insertion.</summary>
/// <typeparam name="TEntity">The hierarchy entity whose generated tokens are projected without payload.</typeparam>
internal sealed class NestedSetGeneratedConcurrency<TEntity>
    where TEntity : class
{
    // WHY: Repeated ordered insertions share immutable selectors without retaining a context, query, or entity.
    private static readonly ConditionalWeakTable<IEntityType, NestedSetGeneratedConcurrency<TEntity>>
        s_entities = new();

    private readonly IProperty[] _properties;
    private readonly Expression<Func<TEntity, object[]>>? _projection;

    /// <summary>Resolves the generated concurrency properties for one finalized entity mapping.</summary>
    /// <param name="entityType">The metadata whose lifetime bounds the cached selector.</param>
    private NestedSetGeneratedConcurrency(
        IEntityType entityType
    )
    {
        // WHY: Complex scalar tokens participate in EF concurrency checks just like root properties.
        _properties = entityType
            .GetFlattenedProperties()
            .Where(property => property.IsConcurrencyToken && (property.ValueGenerated & ValueGenerated.OnUpdate) != 0)
            .ToArray();

        if (_properties.Length == 0)
        {
            return;
        }

        var parameter = Expression.Parameter(typeof(TEntity), "node");

        _projection = Expression.Lambda<Func<TEntity, object[]>>(
            Expression.NewArrayInit(
                typeof(object),
                _properties.Select(property => Expression.Convert(
                    NestedSetExpressions.Property(parameter, property),
                    typeof(object)))),
            parameter);
    }

    /// <summary>Loads only update-generated concurrency values and assigns their final persisted CLR values.</summary>
    /// <param name="query">The untracked, exact-tree-and-key query selecting only the inserted node.</param>
    /// <param name="entry">The already saved entry that the insertion operation will detach before returning.</param>
    /// <param name="cancellationToken">The token for the narrow database projection.</param>
    /// <returns>A completed task without database work when no update-generated concurrency properties exist.</returns>
    /// <remarks>
    ///     The caller retains its transaction and hierarchy write lock until this refresh completes. This helper is
    ///     intended for insertion results; it does not preserve pending payload originals for coordinated saves.
    /// </remarks>
    internal static async Task RefreshAsync(
        IQueryable<TEntity> query,
        EntityEntry entry,
        CancellationToken cancellationToken
    )
    {
        var metadata = s_entities.GetValue(entry.Metadata, static entityType => new(entityType));

        if (metadata._projection is not { } projection)
        {
            return;
        }

        var values = await query
            .Select(projection)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        for (var index = 0; index < metadata._properties.Length; index++)
        {
            // WHY: CurrentValue writes the mapped CLR member; generated sidecars would be lost on detachment.
            NestedSetTrackedProperty.Property(entry, metadata._properties[index]).CurrentValue = values[index];
        }
    }
}

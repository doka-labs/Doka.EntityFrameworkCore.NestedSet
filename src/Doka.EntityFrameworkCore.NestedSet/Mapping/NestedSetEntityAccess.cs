namespace Doka.EntityFrameworkCore.NestedSet.Mapping;

/// <summary>Resolves and accesses ordinary and named shared-type hierarchy entity sets uniformly.</summary>
internal static class NestedSetEntityAccess<TEntity>
    where TEntity : class
{
    /// <summary>Resolves one hierarchy entity by CLR type or explicit shared-type name.</summary>
    internal static IEntityType Resolve(
        DbContext context,
        string? entityTypeName = null
    )
    {
        ArgumentNullException.ThrowIfNull(context);

        if (entityTypeName is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(entityTypeName);
            var named = context.Model.FindEntityType(entityTypeName)
                ?? throw new InvalidOperationException(
                    $"Entity type '{entityTypeName}' is not part of the current EF model.");

            if (named.ClrType != typeof(TEntity))
            {
                throw new InvalidOperationException(
                    $"Entity type '{entityTypeName}' uses CLR type '{named.ClrType.FullName}', not "
                    + $"'{typeof(TEntity).FullName}'.");
            }

            return named;
        }

        var entity = context.Model.FindEntityType(typeof(TEntity));

        if (entity is not null)
        {
            return entity;
        }

        // WHY: A CLR type may back multiple shared entity types. Guessing one would route queries and writes to
        // the wrong table, so shared mappings require EF's stable entity-type name at the public boundary.
        if (context
            .Model
            .GetEntityTypes()
            .Any(candidate => candidate.ClrType == typeof(TEntity)))
        {
            throw new InvalidOperationException(
                $"CLR type '{typeof(TEntity).FullName}' is mapped as a shared type. "
                + "Call NestedSet<TEntity>(entityTypeName) with its unique EF entity-type name.");
        }

        throw new InvalidOperationException(
            $"Entity '{typeof(TEntity).FullName}' is not part of the current EF model.");
    }

    /// <summary>Returns the correctly named EF set for an already resolved entity type.</summary>
    internal static DbSet<TEntity> Set(
        DbContext context,
        IEntityType entityType
    ) => entityType.HasSharedClrType ? context.Set<TEntity>(entityType.Name) : context.Set<TEntity>();

    /// <summary>Returns an entry whose metadata is unambiguous for ordinary and shared CLR types.</summary>
    internal static EntityEntry<TEntity> Entry(
        DbContext context,
        IEntityType entityType,
        TEntity entity
    ) => entityType.HasSharedClrType
        ? Set(context, entityType).Entry(entity)
        : context.Entry(entity);

    /// <summary>Returns tracked entries belonging to this exact EF entity type.</summary>
    internal static IEnumerable<EntityEntry<TEntity>> Entries(
        DbContext context,
        IEntityType entityType
    ) => context
        .ChangeTracker
        .Entries<TEntity>()
        .Where(entry => entityType.IsAssignableFrom(entry.Metadata));
}

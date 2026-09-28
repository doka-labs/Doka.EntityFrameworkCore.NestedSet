namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Provides nested-set entity configuration.</summary>
public static class NestedSetEntityTypeBuilderExtensions
{
    /// <summary>Maps nested-set properties and ensures supporting query indexes.</summary>
    /// <typeparam name="TEntity">The mapped entity type.</typeparam>
    /// <param name="builder">The entity builder to configure.</param>
    /// <param name="configure">The callback that selects nested-set properties.</param>
    /// <returns>The original entity builder.</returns>
    /// <exception cref="ArgumentNullException">The builder or callback is null.</exception>
    /// <exception cref="ArgumentException">A callback selector is not a direct entity property.</exception>
    /// <exception cref="InvalidOperationException">Required nested-set properties are not configured.</exception>
    /// <remarks>
    ///     Enable nested sets through
    ///     <see cref="NestedSetDbContextOptionsBuilderExtensions.UseNestedSets(DbContextOptionsBuilder)" /> so final
    ///     validation, infrastructure, collation capture, and late index reconciliation are applied. Existing databases
    ///     require an ordinary provider migration to receive newly configured indexes; SafeMigrations is optional.
    ///     Generated indexes are nonunique and include the tree identity even when no application scope is configured.
    /// </remarks>
    public static EntityTypeBuilder<TEntity> HasNestedSet<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Action<NestedSetBuilder<TEntity>> configure
    )
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        // WHY: A failed callback must not leave partially applied role annotations on the EF model.
        var configuration = new NestedSetBuilder<TEntity>(builder);
        configure(configuration);
        configuration.Complete();

        return builder;
    }
}

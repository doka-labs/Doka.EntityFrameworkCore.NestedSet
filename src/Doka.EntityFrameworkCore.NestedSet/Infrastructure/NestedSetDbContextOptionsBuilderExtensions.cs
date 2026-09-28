namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Configures nested-set infrastructure for an Entity Framework Core context.</summary>
public static class NestedSetDbContextOptionsBuilderExtensions
{
    /// <summary>Adds nested-set model conventions and infrastructure to typed context options.</summary>
    /// <typeparam name="TContext">The context type being configured.</typeparam>
    /// <param name="optionsBuilder">The typed options builder to configure.</param>
    /// <returns>The same typed options builder so that additional configuration can be chained.</returns>
    /// <exception cref="ArgumentNullException">The options builder is null.</exception>
    public static DbContextOptionsBuilder<TContext> UseNestedSets<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder
    )
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        UseNestedSets((DbContextOptionsBuilder)optionsBuilder);

        return optionsBuilder;
    }

    /// <summary>Adds nested-set model conventions and infrastructure to the context options.</summary>
    /// <param name="optionsBuilder">The options builder to configure.</param>
    /// <returns>The same options builder so that additional configuration can be chained.</returns>
    /// <exception cref="ArgumentNullException">The options builder is null.</exception>
    /// <remarks>
    ///     Registration is idempotent and safe for context pooling. Entity mappings remain configured with
    ///     <c>HasNestedSet</c>; this method removes the need to register nested-set conventions and infrastructure
    ///     separately in <c>ConfigureConventions</c> and <c>OnModelCreating</c>.
    /// </remarks>
    public static DbContextOptionsBuilder UseNestedSets(
        this DbContextOptionsBuilder optionsBuilder
    )
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        // WHY: EF keys options extensions by their concrete type, so replacing the same immutable instance keeps
        // repeated calls allocation-free and gives pooled contexts one stable internal service-provider identity.
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(
            NestedSetOptionsExtension.Instance);

        return optionsBuilder;
    }
}

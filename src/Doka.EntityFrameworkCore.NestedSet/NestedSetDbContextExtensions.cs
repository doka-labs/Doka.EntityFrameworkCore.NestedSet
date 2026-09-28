namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Provides the standard hierarchy entry point on an existing EF Core context.</summary>
public static class NestedSetDbContextExtensions
{
    /// <summary>Saves application changes and atomically coordinates tracked hierarchy changes.</summary>
    /// <param name="context">The caller-owned context.</param>
    /// <param name="acceptAllChangesOnSuccess">Whether to accept tracked changes after the complete boundary succeeds.</param>
    /// <param name="saveChanges">
    ///     A delegate that invokes the application's base <c>SaveChangesAsync(false, token)</c> implementation.
    /// </param>
    /// <param name="cancellationToken">The token used for locking, persistence, and structural updates.</param>
    /// <returns>The saved-entry count reported by Entity Framework Core, excluding structural set updates.</returns>
    /// <exception cref="ArgumentNullException">The context or save delegate is null.</exception>
    /// <exception cref="NestedSetException">The save boundary is recursive or hierarchy work is invalid.</exception>
    public static Task<int> SaveNestedSetChangesAsync(
        this DbContext context,
        bool acceptAllChangesOnSuccess,
        Func<CancellationToken, Task<int>> saveChanges,
        CancellationToken cancellationToken = default
    ) => NestedSetSaveChanges.ExecuteAsync(context, acceptAllChangesOnSuccess, saveChanges, cancellationToken);

    /// <summary>Saves application changes synchronously when no asynchronous hierarchy work is required.</summary>
    /// <param name="context">The caller-owned context.</param>
    /// <param name="acceptAllChangesOnSuccess">Whether to accept tracked changes after the save succeeds.</param>
    /// <param name="saveChanges">A delegate that invokes the application's base <c>SaveChanges(false)</c>.</param>
    /// <returns>The saved-entry count reported by Entity Framework Core.</returns>
    /// <exception cref="ArgumentNullException">The context or save delegate is null.</exception>
    /// <exception cref="NestedSetException">Tracked hierarchy work requires asynchronous coordination.</exception>
    public static int SaveNestedSetChanges(
        this DbContext context,
        bool acceptAllChangesOnSuccess,
        Func<int> saveChanges
    )
    {
        var savedCount = NestedSetSaveChanges.ExecuteSynchronous(context, saveChanges);

        if (acceptAllChangesOnSuccess)
        {
            context.ChangeTracker.AcceptAllChanges();
        }

        return savedCount;
    }

    /// <summary>Creates a metadata-validated, composable facade for one configured hierarchy entity.</summary>
    /// <typeparam name="TEntity">The entity configured through <see cref="NestedSetEntityTypeBuilderExtensions.HasNestedSet{TEntity}(EntityTypeBuilder{TEntity}, Action{NestedSetBuilder{TEntity}})" />.</typeparam>
    /// <param name="context">The caller-owned context.</param>
    /// <returns>A lightweight facade over the context's normal <see cref="DbSet{TEntity}" />.</returns>
    /// <exception cref="InvalidOperationException">The entity is absent or is not configured as a nested set.</exception>
    public static NestedSet<TEntity> NestedSet<TEntity>(
        this DbContext context
    )
        where TEntity : class => new(context);

    /// <summary>Creates a facade for one uniquely named shared-type hierarchy entity.</summary>
    /// <typeparam name="TEntity">The shared CLR type used by the configured hierarchy entity.</typeparam>
    /// <param name="context">The caller-owned context.</param>
    /// <param name="entityTypeName">The unique EF entity-type name supplied when the shared type was configured.</param>
    /// <returns>A lightweight facade over the named shared <see cref="DbSet{TEntity}" />.</returns>
    /// <exception cref="ArgumentException">The entity-type name is empty.</exception>
    /// <exception cref="InvalidOperationException">
    ///     The named entity is absent, uses another CLR type, or is not configured as a nested set.
    /// </exception>
    public static NestedSet<TEntity> NestedSet<TEntity>(
        this DbContext context,
        string entityTypeName
    )
        where TEntity : class => new(context, entityTypeName);
}

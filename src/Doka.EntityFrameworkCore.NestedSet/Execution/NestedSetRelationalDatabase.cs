namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Validates library-owned insertion saves at Entity Framework Core's persistence boundary.</summary>
/// <remarks>
///     EF passes the exact write set to <see cref="IDatabase" /> once per save, after every SavingChanges callback and
///     the final change detection, and before the first command. Queries and ordinary saves add no interception work.
/// </remarks>
internal sealed class NestedSetRelationalDatabase : RelationalDatabase
{
    private readonly ICurrentDbContext _currentContext;

    /// <summary>Creates the relational database service used by every context registered through UseNestedSets.</summary>
    /// <param name="dependencies">The provider-independent database dependencies.</param>
    /// <param name="relationalDependencies">The relational batching, execution, and connection dependencies.</param>
    /// <param name="currentContext">The context whose managed insertion state is checked before persistence.</param>
    public NestedSetRelationalDatabase(
        DatabaseDependencies dependencies,
        RelationalDatabaseDependencies relationalDependencies,
        ICurrentDbContext currentContext
    ) : base(dependencies, relationalDependencies)
    {
        _currentContext = currentContext;
    }

    /// <inheritdoc />
    public override int SaveChanges(
        IList<IUpdateEntry> entries
    )
    {
        NestedSetSaveChanges.ValidateManagedPersistence(_currentContext.Context, entries);

        return base.SaveChanges(entries);
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(
        IList<IUpdateEntry> entries,
        CancellationToken cancellationToken = default
    )
    {
        NestedSetSaveChanges.ValidateManagedPersistence(_currentContext.Context, entries);

        return base.SaveChangesAsync(entries, cancellationToken);
    }
}

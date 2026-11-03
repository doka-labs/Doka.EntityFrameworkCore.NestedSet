namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Validates hierarchy saves at Entity Framework Core's persistence boundary.</summary>
/// <remarks>
///     For each persistence attempt, EF passes its exact write set to <see cref="IDatabase" /> after SavingChanges
///     callbacks and optional final change detection, before the first command. Guards observe it without detecting
///     additional changes or altering the prepared write set. Queries do not use this boundary.
/// </remarks>
internal sealed class NestedSetRelationalDatabase : RelationalDatabase
{
    private readonly ICurrentDbContext _currentContext;

    /// <summary>Creates the relational service used by every context registered through UseNestedSets.</summary>
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
        var completion = NestedSetSaveChanges.PersistenceCompletion(_currentContext.Context, entries);
        var result = base.SaveChanges(entries);
        completion.Insertion?.Invoke();
        completion.Save?.Invoke();

        return result;
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(
        IList<IUpdateEntry> entries,
        CancellationToken cancellationToken = default
    )
    {
        NestedSetSaveChanges.ValidateManagedPersistence(_currentContext.Context, entries);
        var completion = NestedSetSaveChanges.PersistenceCompletion(_currentContext.Context, entries);

        return completion is (null, null)
            ? base.SaveChangesAsync(entries, cancellationToken)
            : CompleteManagedAsync(entries, completion, cancellationToken);
    }

    /// <summary>Records persisted identities and relationships before EF acceptance and SavedChanges.</summary>
    /// <param name="entries">The exact validated provider write set.</param>
    /// <param name="completion">The active identity and relationship callbacks.</param>
    /// <param name="cancellationToken">The caller's provider-operation cancellation token.</param>
    /// <returns>The provider's unchanged affected-entry count.</returns>
    private async Task<int> CompleteManagedAsync(
        IList<IUpdateEntry> entries,
        (Action? Insertion, Action? Save) completion,
        CancellationToken cancellationToken
    )
    {
        // WHY: Store-generated identities exist when the relational service returns; EF acceptance and
        // SavedChanges happen afterward. Record only identities and relationships proven at this boundary.
        var result = await base.SaveChangesAsync(entries, cancellationToken).ConfigureAwait(false);
        completion.Insertion?.Invoke();
        completion.Save?.Invoke();

        return result;
    }
}

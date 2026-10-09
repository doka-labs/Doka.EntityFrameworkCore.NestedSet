namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Validates library-owned insertion saves at Entity Framework Core's persistence boundary.</summary>
/// <remarks>
///     EF passes the exact write set to <see cref="IDatabase" /> once per save, after every SavingChanges callback and
///     the final change detection, and before the first command. Queries and ordinary saves add no interception work.
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
        var completion = NestedSetSaveChanges.ManagedPersistenceCompletion(_currentContext.Context);
        var result = base.SaveChanges(entries);
        completion?.Invoke();

        return result;
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(
        IList<IUpdateEntry> entries,
        CancellationToken cancellationToken = default
    )
    {
        NestedSetSaveChanges.ValidateManagedPersistence(_currentContext.Context, entries);
        var completion = NestedSetSaveChanges.ManagedPersistenceCompletion(_currentContext.Context);

        return completion is null
            ? base.SaveChangesAsync(entries, cancellationToken)
            : CompleteManagedAsync(entries, completion, cancellationToken);
    }

    /// <summary>Captures native identities before EF accepts provider results and publishes SavedChanges.</summary>
    /// <param name="entries">The exact validated provider write set.</param>
    /// <param name="completion">The bounded insertion identity callback.</param>
    /// <param name="cancellationToken">The caller's provider-operation cancellation token.</param>
    /// <returns>The provider's unchanged affected-entry count.</returns>
    private async Task<int> CompleteManagedAsync(
        IList<IUpdateEntry> entries,
        Action completion,
        CancellationToken cancellationToken
    )
    {
        // WHY: Store-generated identities exist when the relational service returns; EF acceptance and
        // SavedChanges happen afterward. Stabilize only managed insertion keys at this existing boundary.
        var result = await base.SaveChangesAsync(entries, cancellationToken).ConfigureAwait(false);
        completion();

        return result;
    }
}

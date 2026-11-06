namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Shares one validated operation's reference proof between save guards and failure cleanup.</summary>
/// <param name="Matches">The complete future-FK proof, without authorizing an EF state transition.</param>
/// <param name="Mapping">The exact input reference mapping, or null for foreign objects.</param>
/// <param name="ForeignKeys">The operation's potential external collection relationships.</param>
/// <param name="EntityType">The exact configured root mapping, including its stable shared-type name.</param>
internal sealed record NestedSetImportInputs(
    Func<IUpdateEntry, IForeignKey, object, bool> Matches,
    Func<object, IEntityType?> Mapping,
    HashSet<IForeignKey> ForeignKeys,
    IEntityType EntityType
)
{
    private HashSet<object>? _collectionScratch;

    /// <summary>Reuses reference membership storage bounded to still-tracked collection snapshot members.</summary>
    internal HashSet<object> CollectionScratch =>
        _collectionScratch ??= new HashSet<object>(ReferenceEqualityComparer.Instance);

    /// <summary>Releases every probed reference after a guard or its owning import scope completes.</summary>
    internal void ClearCollectionScratch() => _collectionScratch?.Clear();

    /// <summary>Detaches only callback-tracked known inputs outside the already owned active batch.</summary>
    internal void DetachUnexpected(
        DbContext context,
        IEnumerable<IUpdateEntry> active
    )
    {
        var activeEntries = new HashSet<IUpdateEntry>(active, ReferenceEqualityComparer.Instance);
        var updates = context
            .GetService<IUpdateAdapterFactory>()
            .Create();

        var entries = updates.Entries.ToArray();
        var roots = new HashSet<IUpdateEntry>(ReferenceEqualityComparer.Instance);

        foreach (var entry in entries)
        {
            if (!activeEntries.Contains(entry)
                && !entry.EntityType.IsOwned()
                && EntityType.IsAssignableFrom(entry.EntityType)
                && Mapping(
                    entry.ToEntityEntry()
                        .Entity)
                == entry.EntityType)
            {
                roots.Add(entry);
            }
        }

        if (roots.Count == 0)
        {
            return;
        }

        var owned = new Dictionary<IUpdateEntry, bool>(ReferenceEqualityComparer.Instance);

        var selected = entries
            .Where(BelongsToRoot)
            .ToArray();

        var introduced = new List<NestedSetInsertionTracking.Identity>(selected.Length);
        List<Exception>? errors = null;

        foreach (var entry in selected)
        {
            try
            {
                introduced.Add(NestedSetInsertionTracking.Capture(entry.ToEntityEntry()));
            }
            catch (Exception error)
            {
                (errors ??= []).Add(error);

                try
                {
                    NestedSetInsertionTracking.Detach(entry.ToEntityEntry());
                }
                catch (Exception detachError)
                {
                    errors.Add(detachError);
                }
            }
        }

        foreach (var identity in introduced)
        {
            try
            {
                identity.PrepareDetach();
            }
            catch (Exception error)
            {
                (errors ??= []).Add(error);
            }
        }

        // WHY: Only known inputs and their modeled owned payload are released. Arbitrary callback-created
        // application or hierarchy entities remain under their creator's ownership.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = introduced.Count - 1; index >= 0; index--)
        {
            try
            {
                introduced[index]
                    .Detach();
            }
            catch (Exception error)
            {
                (errors ??= []).Add(error);
            }
        }

        try
        {
            NestedSetInsertionTracking.VerifyFailedCleanup(selected.Select(entry => entry.ToEntityEntry()));
        }
        catch (Exception error)
        {
            (errors ??= []).Add(error);
        }

        if (errors is not null)
        {
            throw new AggregateException("Import input cleanup failed. Discard the context.", errors);
        }

        return;

        bool BelongsToRoot(
            IUpdateEntry entry
        )
        {
            if (roots.Contains(entry))
            {
                return true;
            }

            if (activeEntries.Contains(entry)
                || !entry.EntityType.IsOwned())
            {
                return false;
            }

            if (owned.TryGetValue(entry, out var known))
            {
                return known;
            }

            var ownership = entry.EntityType.FindOwnership()!;
            var principal = updates.FindPrincipal(entry, ownership);

            if (principal is null
                && ownership.DependentToPrincipal is { } reference
                && entry.GetCurrentValue(reference) is { } entity)
            {
                var contract = NestedSetInsertionContract.Instance;
                principal = contract.FindTrackedEntry(contract.Manager(entry), entity, reference.TargetEntityType);
            }

            var result = principal is not null && BelongsToRoot(principal);
            owned.Add(entry, result);

            return result;
        }
    }
}

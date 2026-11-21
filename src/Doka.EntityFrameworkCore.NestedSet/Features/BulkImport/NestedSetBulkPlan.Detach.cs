namespace Doka.EntityFrameworkCore.NestedSet.Features.BulkImport;

internal sealed partial class NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    /// <summary>Detaches only entities owned by this import, retaining unrelated unchanged tracked entries.</summary>
    internal void Detach()
    {
        if (DetachActiveBatch() is { } errors)
        {
            throw new AggregateException("One or more imported entities could not be detached.", errors);
        }
    }

    /// <summary>Attempts every owned detachment and retains failures for the operation boundary to aggregate.</summary>
    private List<Exception>? DetachActiveBatch(
        bool failed = false
    )
    {
        if (_activeEntries is not { } entries)
        {
            return null;
        }

        List<Exception>? errors = null;
        Detach(entries, _activeIdentities!, ref errors);

        if (failed)
        {
            VerifyFailedCleanup(entries, ref errors);
        }

        if (_activeRelationships is { } relationships)
        {
            RestoreRelationships(relationships, ref errors);
        }

        if (errors is not null)
        {
            // WHY: StateChanging can reject detachment before EF changes the entry state. Keep the bounded
            // batch handles available for rollback cleanup instead of losing ownership of still-tracked rows.
            return errors;
        }

        _activeEntries = null;
        _activeIdentities = null;
        _activeRelationships = null;
        _activeRoots = null;
        _activeOffset = 0;

        return null;
    }

    /// <summary>Detaches a bounded entry set in reverse dependency order.</summary>
    private static void Detach(
        IReadOnlyList<EntityEntry> entries,
        IReadOnlyDictionary<IUpdateEntry, NestedSetInsertionTracking.Identity> identities,
        ref List<Exception>? errors
    )
    {
        // WHY: Index this stable list directly without creating an enumerator or a copied buffer.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (int entryIndex = 0, entryCount = entries.Count; entryIndex < entryCount; entryIndex++)
        {
            var entry = entries[entryIndex];

            if (identities.TryGetValue(NestedSetInsertionTracking.EntryIdentity(entry), out var identity))
            {
                try
                {
                    identity.PrepareDetach();
                }
                catch (Exception exception)
                {
                    (errors ??= []).Add(exception);
                }
            }
        }

        for (var index = entries.Count - 1; index >= 0; index--)
        {
            try
            {
                if (identities.TryGetValue(NestedSetInsertionTracking.EntryIdentity(entries[index]), out var identity))
                {
                    identity.Detach();
                }
                else
                {
                    NestedSetInsertionTracking.Detach(entries[index]);
                }
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }
        }
    }

    /// <summary>Detaches a partial batch while preserving its original exception.</summary>
    private static void Detach(
        IReadOnlyList<EntityEntry> entries,
        IReadOnlyDictionary<IUpdateEntry, NestedSetInsertionTracking.Identity> identities,
        IReadOnlyList<(EntityEntry Owner, INavigationBase Navigation, object Entity)> relationships
    )
    {
        List<Exception>? errors = null;
        Detach(entries, identities, ref errors);
        VerifyFailedCleanup(entries, ref errors);
        RestoreRelationships(relationships, ref errors);

        if (errors is not null)
        {
            throw new AggregateException("One or more imported entities could not be detached.", errors);
        }
    }

    /// <summary>Checks only the failed batch's touched native maps without replacing its original error.</summary>
    private static void VerifyFailedCleanup(
        IReadOnlyList<EntityEntry> entries,
        ref List<Exception>? errors
    )
    {
        try
        {
            NestedSetInsertionTracking.VerifyFailedCleanup(entries);
        }
        catch (Exception error)
        {
            (errors ??= []).Add(error);
        }
    }

    /// <summary>
    ///     Rejects tracking and navigation graphs that would introduce entities outside the explicit topology.
    /// </summary>
    private static void RequireDetached(
        TEntity entity,
        HashSet<TEntity> tracked,
        IEntityType entityType
    )
    {
        if (tracked.Contains(entity))
        {
            throw new NestedSetException(NestedSetErrorCode.InvalidImport, "Every imported entity must be detached.");
        }

        foreach (var navigation in entityType
            .GetNavigations()
            .Cast<INavigationBase>()
            .Concat(entityType.GetSkipNavigations()))
        {
            var value = navigation
                .GetGetter()
                .GetClrValue(entity);

            if (value is not null
                && (value is not IEnumerable sequence
                    || sequence
                        .Cast<object>()
                        .Any())
                && !navigation.TargetEntityType.IsOwned())
            {
                throw new NestedSetException(
                    NestedSetErrorCode.InvalidImport,
                    "Import entities without populated non-owned navigation properties.");
            }
        }
    }
}

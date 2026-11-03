using System.Runtime.InteropServices;

namespace Doka.EntityFrameworkCore.NestedSet.Features.BulkImport;

/// <summary>
/// Retains application writes accepted by library-owned insertion saves until the operation completes.
/// </summary>
internal sealed class NestedSetCallbackWrites
{
    private List<NestedSetTrackerSnapshot>? _snapshots;
    private NestedSetCollectionBaseline? _relationships;
    private NestedSetInputNavigations? _inputNavigations;
    private DbContext? _importContext;
    private NestedSetImportInputs? _inputs;

    /// <summary>Preserves only potential external owners already containing a validated operation input.</summary>
    internal void CaptureInputOwners(
        DbContext context,
        NestedSetImportInputs inputs
    )
    {
        _importContext = context;
        _inputs = inputs;
        NestedSetCollectionBaseline.CaptureInputOwners(context, inputs, ref _relationships);
    }

    /// <summary>Captures an external principal's native collection baseline before tracking an input.</summary>
    internal void CaptureRelationships(
        EntityEntry entry
    )
    {
        NestedSetCollectionBaseline.CaptureBeforeTracking(entry, ref _relationships);
        NestedSetInputNavigations.Capture(entry, ref _inputNavigations);
    }

    /// <summary>Baselines exact saved relationships before a bounded insertion wave leaves the tracker.</summary>
    /// <param name="identities">The active wave's identities, consumed exactly once.</param>
    /// <param name="introducedEntries">The exact native insertion set already validated by the save guard.</param>
    /// <param name="introducedEntities">The corresponding reference set already owned by the save plan.</param>
    internal void RecordSavedRelationships(
        IEnumerable<NestedSetInsertionTracking.Identity> identities,
        IReadOnlySet<IUpdateEntry> introducedEntries,
        IReadOnlySet<object> introducedEntities
    )
    {
        List<NestedSetInsertionTracking.Identity>? related = null;

        if (identities is NestedSetInsertionTracking.Identity[] array)
        {
            related = CompleteSavedIdentities(array);
        }
        else if (identities is List<NestedSetInsertionTracking.Identity> list)
        {
            // WHY: This synchronous completion never changes the identity list. Its existing storage avoids
            // both a copy and repeated interface indexing on a materialized batch.
            related = CompleteSavedIdentities(CollectionsMarshal.AsSpan(list));
        }
        else if (identities is IReadOnlyList<NestedSetInsertionTracking.Identity> batch)
        {
            // WHY: Read-only list indexing avoids an interface enumerator without copying the batch.
            // Keep the no-relationship path free of a materialized batch or candidate buffer.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < batch.Count; index++)
            {
                CompleteSavedIdentity(batch[index], ref related);
            }
        }
        else
        {
            foreach (var identity in identities)
            {
                CompleteSavedIdentity(identity, ref related);
            }
        }

        if (related is null)
        {
            return;
        }

        // WHY: Complete every input navigation before resolving saved owners, as before. Only relevant
        // identities need retention; both exclusion sets already belong to the validated persistence plan.
        var saved = new NestedSetCollectionBaseline.SavedRelationships(introducedEntries, introducedEntities);

        foreach (var identity in related)
        {
            identity.CollectSavedRelationships(saved);
        }

        saved.Record(ref _relationships);
    }

    /// <summary>Visits stable concrete batch storage without interface indexing or a boxed enumerator.</summary>
    /// <param name="identities">The existing array or list storage for this synchronous completion.</param>
    /// <returns>The relevant subset, or null when the allocation-free early return applies.</returns>
    private List<NestedSetInsertionTracking.Identity>? CompleteSavedIdentities(
        ReadOnlySpan<NestedSetInsertionTracking.Identity> identities
    )
    {
        List<NestedSetInsertionTracking.Identity>? related = null;

        if (_inputNavigations is null)
        {
            // WHY: Without captured input collections there is nothing to complete. Keep that null check
            // outside the measured no-relationship loop instead of repeating it for every identity.
            foreach (var identity in identities)
            {
                if (identity.HasCollectionRelationships)
                {
                    (related ??= []).Add(identity);
                }
            }
        }
        else
        {
            foreach (var identity in identities)
            {
                CompleteSavedIdentity(identity, ref related);
            }
        }

        return related;
    }

    /// <summary>Completes one input and retains it only when a saved collection relationship can exist.</summary>
    /// <param name="identity">The current wave's native lifecycle handle.</param>
    /// <param name="related">The lazily allocated relevant subset of the current wave.</param>
    private void CompleteSavedIdentity(
        NestedSetInsertionTracking.Identity identity,
        ref List<NestedSetInsertionTracking.Identity>? related
    )
    {
        _inputNavigations?.Complete(identity);

        if (identity.HasCollectionRelationships)
        {
            (related ??= []).Add(identity);
        }
    }

    /// <summary>Records one save's pre-persistence callback writes in persistence order.</summary>
    /// <param name="snapshot">The captured writes, or null when callbacks added no ordinary write.</param>
    internal void Add(
        NestedSetTrackerSnapshot? snapshot
    )
    {
        if (snapshot is not null)
        {
            (_snapshots ??= []).Add(snapshot);
        }
    }

    /// <summary>Restores every recorded write to its pending state after the owning transaction rolled back.</summary>
    /// <exception cref="AggregateException">One or more entries could not be restored; discard the context.</exception>
    internal void Restore()
    {
        List<Exception>? errors = null;

        // WHY: A later batch can modify an entry that an earlier batch already accepted. Restoring newest first
        // leaves the earliest pending original values, which match the rolled-back database row.
        for (var index = (_snapshots?.Count ?? 0) - 1; index >= 0; index--)
        {
            try
            {
                _snapshots![index].Restore();
            }
            catch (Exception error)
            {
                (errors ??= []).Add(error);
            }
        }

        try
        {
            if (_importContext is not null
                && _inputs is not null)
            {
                NestedSetCollectionBaseline.RemoveUnexpectedInputs(_importContext, _inputs, _relationships);
            }
        }
        catch (Exception error)
        {
            (errors ??= []).Add(error);
        }

        try
        {
            _inputNavigations?.Restore();
        }
        catch (Exception error)
        {
            (errors ??= []).Add(error);
        }

        try
        {
            _relationships?.Restore();
        }
        catch (Exception error)
        {
            (errors ??= []).Add(error);
        }

        if (errors is not null)
        {
            throw new AggregateException("One or more save callback writes could not be restored.", errors);
        }
    }
}

namespace Doka.EntityFrameworkCore.NestedSet.Features.Insert;

internal sealed partial class NestedSetInsert<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    /// <summary>Attempts exact lifecycle cleanup for every introduced entry without abandoning owned payload.</summary>
    /// <param name="entries">Only the entries introduced for this bounded insertion aggregate.</param>
    private static void DetachInsertionGraph(
        IReadOnlyList<(EntityEntry Entry, NestedSetInsertionTracking.Identity? Identity)> entries
    )
    {
        List<Exception>? failures = null;

        foreach (var (_, identity) in entries)
        {
            try
            {
                // WHY: Mutable principal keys must match their installed identities before public detachment
                // performs owned relationship fixup. Prepare the whole aggregate before detaching any entry.
                identity?.PrepareDetach();
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
            }
        }

        for (var index = entries.Count - 1; index >= 0; index--)
        {
            try
            {
                if (entries[index].Identity is { } identity)
                {
                    identity.Detach();
                }
                else
                {
                    NestedSetInsertionTracking.Detach(entries[index].Entry);
                }
            }
            catch (Exception error)
            {
                // WHY: An application lifecycle callback must not prevent cleanup of the remaining aggregate.
                (failures ??= []).Add(error);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException("The inserted aggregate could not be completely detached.", failures);
        }
    }

    /// <summary>Attempts all owned cleanup steps while retaining every application callback failure.</summary>
    /// <param name="entry">The original detached root entry used to restore its mapped structure.</param>
    /// <param name="insertionEntries">The exact aggregate entries owned by this insertion attempt.</param>
    /// <param name="insertionRelationships">The caller's original owned navigation memberships.</param>
    /// <param name="original">The independent snapshots of every caller-visible structural role.</param>
    /// <param name="insertionValues">The independent generated-leaf and owned-identity snapshots.</param>
    private static void Restore(
        EntityEntry entry,
        IReadOnlyList<(EntityEntry Entry, NestedSetInsertionTracking.Identity? Identity)> insertionEntries,
        IReadOnlyList<(EntityEntry Owner, INavigationBase Navigation, object Entity)> insertionRelationships,
        Dictionary<IProperty, object?> original,
        IReadOnlyList<NestedSetInsertionValues.Snapshot> insertionValues
    )
    {
        List<Exception>? failures = null;

        try
        {
            DetachInsertionGraph(insertionEntries);

            if (!insertionEntries.Any(candidate => ReferenceEquals(candidate.Entry.Entity, entry.Entity))
                || entry.State != EntityState.Detached)
            {
                NestedSetInsertionTracking.Detach(entry);
            }
        }
        catch (Exception error)
        {
            (failures ??= []).Add(error);
        }

        try
        {
            // WHY: Repeated callback-owned mutable rekeys can corrupt native hash slots. A failed attempt
            // audits only maps touched by its exact entries; any residual identity requires discarding the context.
            NestedSetInsertionTracking.VerifyFailedCleanup(insertionEntries.Select(candidate => candidate.Entry));
        }
        catch (Exception error)
        {
            (failures ??= []).Add(error);
        }

        foreach (var (property, value) in original)
        {
            try
            {
                // WHY: EF's ordinary setter can skip a comparer-equal alias or clone. Restore exact mapped CLR
                // values, including independent mutable keys, after removing this operation's tracked entry.
                if (!NestedSetInsertionValues.RestoreDictionaryValue(entry.Entity, property, value))
                {
                    NestedSetStructuralValue.Assign(
                        entry,
                        property,
                        NestedSetStructuralValue.Snapshot(property, value));
                }
            }
            catch (Exception error)
            {
                // WHY: One rejecting setter must not prevent restoration of the remaining managed properties.
                (failures ??= []).Add(error);
            }
        }

        foreach (var (owner, navigation, entity) in insertionRelationships)
        {
            try
            {
                // WHY: Detaching a mutable-key aggregate can sever its owned CLR relationships. Restore the
                // caller's original graph after every entry is detached so retry can save the same owned payload.
                NestedSetInsertionTracking.RestoreRelationship(owner.Entity, navigation, entity);
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
            }
        }

        foreach (var snapshot in insertionValues)
        {
            try
            {
                snapshot.Restore();
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
            }
        }

        if (entry.State != EntityState.Detached)
        {
            try
            {
                NestedSetInsertionTracking.Detach(entry);
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException("The inserted entity could not be completely restored.", failures);
        }
    }
}

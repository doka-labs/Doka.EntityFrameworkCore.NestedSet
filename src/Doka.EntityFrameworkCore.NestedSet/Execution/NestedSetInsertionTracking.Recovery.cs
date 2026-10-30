namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal static partial class NestedSetInsertionTracking
{
    /// <summary>Restores the caller's owned membership without creating detached EF references.</summary>
    /// <param name="owner">The introduced aggregate owner whose membership belonged to this insertion.</param>
    /// <param name="navigation">The exact mapped ownership navigation or its inverse.</param>
    /// <param name="entity">The original aggregate member whose reference must remain reusable after rollback.</param>
    internal static void RestoreRelationship(
        object owner,
        INavigationBase navigation,
        object entity
    )
    {
        if (navigation.IsCollection)
        {
            navigation.GetCollectionAccessor()!.Add(owner, entity, forMaterialization: false);
        }
        else
        {
            // WHY: NavigationEntry.CurrentValue can create new strong Detached reference-map entries while
            // restoring an already-detached aggregate. EF's model-owned CLR setter preserves exact member
            // access without another tracker lifecycle or context-retained application reference.
            NestedSetDetachedValueSetter.Get(navigation)!.SetClrValueUsingContainingEntity(owner, entity);
        }
    }

    /// <summary>Rejects a failed insertion's surviving native identities after exact lifecycle cleanup.</summary>
    /// <param name="entries">Only the aggregate entries introduced by this failed insertion.</param>
    internal static void VerifyFailedCleanup(
        IEnumerable<EntityEntry> entries
    )
    {
        var introduced = new HashSet<IUpdateEntry>(ReferenceEqualityComparer.Instance);
        var maps = new HashSet<object>(ReferenceEqualityComparer.Instance);

        foreach (var entry in entries)
        {
            var update = EntryIdentity(entry);
            introduced.Add(update);
            var manager = StateManager(update);

            foreach (var key in entry.Metadata.GetKeys())
            {
                if (Contract.FindMap(manager, key) is { } map)
                {
                    maps.Add(map);
                }
            }
        }

        var retained = false;
        List<Exception>? errors = null;

        foreach (var map in maps)
        {
            try
            {
                // WHY: Callback-owned DetectChanges can reinstall a live mutable hash key, then a second
                // in-place edit can strand it while a final detection installs another valid current slot.
                // Exact key lookups cannot prove cleanup in that case. Only failed attempts enumerate their
                // touched maps once, matching this bounded entry set without clearing or changing other rows.
                // ReSharper disable once LoopCanBeConvertedToQuery
                foreach (var entry in Contract
                             .Map(map.GetType())
                             .Entries(map))
                {
                    retained |= introduced.Contains(entry);
                }
            }
            catch (Exception error)
            {
                (errors ??= []).Add(error);
            }
        }

        if (retained)
        {
            (errors ??= []).Add(
                new InvalidOperationException(
                    "An insertion callback left a corrupted native EF identity after exact rollback cleanup. "
                    + "Repeated in-place key changes around DetectChanges can invalidate stored hash keys. "
                    + "Discard the context."));
        }

        if (errors is not null)
        {
            throw new AggregateException(
                "Insertion identity cleanup could not be verified. Discard the context.",
                errors);
        }
    }

    /// <summary>Uses normal detachment for tracked entries and removes a never-tracked entry's reference.</summary>
    /// <param name="entry">The exact entry introduced by the current bounded insertion batch.</param>
    internal static void Detach(
        EntityEntry entry
    )
    {
        if (entry.State != EntityState.Detached)
        {
            entry.State = EntityState.Detached;

            return;
        }

        // WHY: Tracking or identity installation can throw before Detached becomes Added. Reassigning Detached
        // is then an EF no-op. Native cleanup removes only this entry's partial identity and reference storage,
        // without fake state transitions, lifecycle callbacks, or clearing unrelated tracker state.
        var contract = Contract;
        var updateEntry = contract.Entry(entry);
        var stateManager = contract.Manager(updateEntry);
        var updates = entry
            .Context
            .GetService<IUpdateAdapterFactory>()
            .Create();

        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var key in entry.Metadata.GetKeys())
        {
            var values = key
                .Properties
                .Select(updateEntry.GetCurrentValue)
                .ToArray();

            if (ReferenceEquals(updates.TryGetEntry(key, values), updateEntry))
            {
                // WHY: A later alternate-key conflict can leave the primary identity installed. EF's native
                // removal compares entry identity, preserving the legitimate entity owning the colliding key.
                contract.StopTracking(stateManager, updateEntry, EntityState.Detached);

                break;
            }
        }

        // WHY: Tracking callbacks fail before the reference moves; identity-map failures happen after it moves
        // to Added. Both registrations belong to this exact still-Detached input and neither removal adds state.
        contract.UpdateReferences(stateManager, updateEntry, EntityState.Detached, EntityState.Added);
        contract.UpdateReferences(stateManager, updateEntry, EntityState.Detached, EntityState.Detached);
    }
}

namespace Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

internal static partial class NestedSetSaveChanges
{
    /// <summary>Observes only relationship differences that can still affect tracked hierarchy state.</summary>
    private static bool NavigationChangesHierarchy(
        IUpdateEntry entry,
        NavigationProbe probe,
        NestedSetModelMapping model,
        IUpdateAdapter updates,
        Dictionary<IUpdateEntry, CandidatePlan>? planned,
        NestedSetImportInputs? importInputs
    )
    {
        var navigation = probe.Navigation;

        if (navigation.IsCollection)
        {
            return CollectionChangesHierarchy(entry, probe, model, updates, planned, importInputs);
        }

        var current = entry.GetCurrentValue(navigation);
        var original = entry.GetRelationshipSnapshotValue(navigation);

        if (ReferenceEquals(current, original))
        {
            return false;
        }

        if (navigation.IsOnDependent)
        {
            var principal = current is null
                ? null
                : TrackedNavigationEntry(entry, current, navigation.TargetEntityType);

            return current is null
                ? NullingChangesHierarchy(
                    entry,
                    original is null ? null : TrackedNavigationEntry(entry, original, navigation.TargetEntityType),
                    navigation.ForeignKey,
                    model,
                    updates,
                    planned)
                : ForeignKeyChangesHierarchy(
                    entry,
                    principal,
                    current,
                    navigation.ForeignKey,
                    model,
                    updates,
                    planned);
        }

        if (original is not null
            && (probe.IncludesDependent?.Invoke(original) ?? true)
            && TrackedNavigationEntry(entry, original, navigation.TargetEntityType) is { } removed
            && NullingChangesHierarchy(removed, entry, navigation.ForeignKey, model, updates, planned))
        {
            return true;
        }

        if (current is null
            || !(probe.IncludesDependent?.Invoke(current) ?? true))
        {
            return false;
        }

        var added = TrackedNavigationEntry(entry, current, navigation.TargetEntityType);

        return added is null
            || ForeignKeyChangesHierarchy(
                added,
                entry,
                entry.ToEntityEntry()
                    .Entity,
                navigation.ForeignKey,
                model,
                updates,
                planned);
    }

    /// <summary>Compares reference sets while ignoring removed objects that EF no longer tracks.</summary>
    private static bool CollectionChangesHierarchy(
        IUpdateEntry entry,
        NavigationProbe probe,
        NestedSetModelMapping model,
        IUpdateAdapter updates,
        Dictionary<IUpdateEntry, CandidatePlan>? planned,
        NestedSetImportInputs? importInputs
    )
    {
        var current = entry.GetCurrentValue(probe.Navigation);
        var original = entry.GetRelationshipSnapshotValue(probe.Navigation);
        var items = current as IEnumerable<object> ?? [];
        var snapshot = original as ISet<object>;

        if (importInputs is not null)
        {
            return ImportCollectionChangesHierarchy(
                entry,
                probe,
                model,
                updates,
                planned,
                importInputs,
                items,
                snapshot);
        }

        // WHY: The navigation collection is revisitable during this synchronous guard. Probe before building
        // another reference set so unchanged/empty collections return early, preserving EF's set semantics.
        // ReSharper disable once PossibleMultipleEnumeration
        if (snapshot is not null
            && snapshot.SetEquals(items))
        {
            return false;
        }

        // ReSharper disable once PossibleMultipleEnumeration
        if (snapshot is null
            && !items.Any())
        {
            return false;
        }

        // ReSharper disable once PossibleMultipleEnumeration
        var members = items.ToHashSet(ReferenceEqualityComparer.Instance);

        // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
        foreach (var member in members)
        {
            if ((snapshot is null || !snapshot.Contains(member))
                && (probe.IncludesDependent?.Invoke(member) ?? true))
            {
                // WHY: EF attaches new untracked members during detection. Ignoring additions would admit
                // hierarchy writes. Tracked members can instead change only ordinary composite-FK payload.
                var added = TrackedNavigationEntry(entry, member, probe.Navigation.TargetEntityType);

                if (added is null)
                {
                    return true;
                }
                else if (ForeignKeyChangesHierarchy(
                             added,
                             entry,
                             entry.ToEntityEntry().Entity,
                             probe.Navigation.ForeignKey,
                             model,
                             updates,
                             planned))
                {
                    return true;
                }
            }
        }

        if (snapshot is null)
        {
            return false;
        }

        foreach (var member in snapshot)
        {
            if (!members.Contains(member)
                && (probe.IncludesDependent?.Invoke(member) ?? true)
                && TrackedNavigationEntry(entry, member, probe.Navigation.TargetEntityType) is { } removed
                && NullingChangesHierarchy(removed, entry, probe.Navigation.ForeignKey, model, updates, planned))
            {
                // WHY: EF deliberately leaves detached removed members in its relationship snapshot. They must
                // not permanently block this context; only a still-tracked dependent can receive FK fixup.
                return true;
            }
        }

        return false;
    }

    /// <summary>Streams prepared import collections while retaining only still-tracked removal candidates.</summary>
    private static bool ImportCollectionChangesHierarchy(
        IUpdateEntry entry,
        NavigationProbe probe,
        NestedSetModelMapping model,
        IUpdateAdapter updates,
        Dictionary<IUpdateEntry, CandidatePlan>? planned,
        NestedSetImportInputs inputs,
        IEnumerable<object> items,
        ISet<object>? snapshot
    )
    {
        var remaining = inputs.CollectionScratch;

        try
        {
            if (snapshot is not null)
            {
                foreach (var member in snapshot)
                {
                    if ((probe.IncludesDependent?.Invoke(member) ?? true)
                        && TrackedNavigationEntry(entry, member, probe.Navigation.TargetEntityType) is not null)
                    {
                        remaining.Add(member);
                    }
                }
            }

            // WHY: A prepared owner may contain the entire import while only one bounded wave is tracked.
            // Retaining future/completed members or SetEquals scratch would scale with the complete input.
            foreach (var member in items)
            {
                remaining.Remove(member);

                if (snapshot?.Contains(member) == true
                    || !(probe.IncludesDependent?.Invoke(member) ?? true))
                {
                    continue;
                }

                var added = TrackedNavigationEntry(entry, member, probe.Navigation.TargetEntityType);

                if (added is null)
                {
                    // WHY: Input membership alone never authorizes FK effects; the proof uses the bound plan.
                    if (!inputs.Matches(entry, probe.Navigation.ForeignKey, member))
                    {
                        return true;
                    }
                }
                else if (ForeignKeyChangesHierarchy(
                             added,
                             entry,
                             entry.ToEntityEntry().Entity,
                             probe.Navigation.ForeignKey,
                             model,
                             updates,
                             planned))
                {
                    return true;
                }
            }

            foreach (var member in remaining)
            {
                if (TrackedNavigationEntry(entry, member, probe.Navigation.TargetEntityType) is { } removed
                    && NullingChangesHierarchy(removed, entry, probe.Navigation.ForeignKey, model, updates, planned))
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            inputs.ClearCollectionScratch();
        }
    }

    /// <summary>Checks relevant FK effects instead of principal reference equality.</summary>
    private static bool ForeignKeyChangesHierarchy(
        IUpdateEntry dependent,
        IUpdateEntry? principal,
        object principalEntity,
        IForeignKey foreignKey,
        NestedSetModelMapping model,
        IUpdateAdapter updates,
        Dictionary<IUpdateEntry, CandidatePlan>? planned
    )
    {
        if (foreignKey.IsUnique
            && principal is not null)
        {
            // WHY: Even equal guarded FK values can steal a unique owner from another hierarchy node.
            // Match EF's first dependent and inspect its severance before treating this as payload-only work.
            var victim = updates
                .GetDependents(principal, foreignKey)
                .FirstOrDefault();

            if (victim is not null
                && !ReferenceEquals(victim, dependent)
                && model.TryDescriptor(victim.EntityType, out _)
                && NullingChangesHierarchy(victim, principal, foreignKey, model, updates, planned))
            {
                return true;
            }
        }

        var structure = model.StructuralProperties(dependent.EntityType);
        var ordering = model.Ordering(dependent.EntityType);

        // WHY: Principal-key and dependent-FK ordinals correspond. Only hierarchy/order FK members can request
        // coordination; another member of the same composite relationship may be ordinary application payload.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < foreignKey.Properties.Count; index++)
        {
            var property = foreignKey.Properties[index];

            if (!structure.Contains(property)
                && ordering?.Properties.Contains(property) != true)
            {
                continue;
            }

            var key = foreignKey.PrincipalKey.Properties[index];
            object? value;

            if (principal is not null)
            {
                value = principal.GetCurrentValue(key);
            }
            else
            {
                if (key.IsShadowProperty())
                {
                    // WHY: An untracked CLR object has no trustworthy shadow-key storage to prove equality.
                    return true;
                }

                var getter = key.GetGetter();

                if ((key.ValueGenerated & ValueGenerated.OnAdd) != 0
                    && getter.HasSentinelValueUsingContainingEntity(principalEntity))
                {
                    // WHY: EF can replace a generated-key sentinel while attaching this principal.
                    return true;
                }

                value = getter.GetClrValueUsingContainingEntity(principalEntity);
            }

            if (value is null
                || !key
                    .GetKeyValueComparer()
                    .Equals(ParentValue(dependent, property, planned), value))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Finds an existing tracked reference without creating entries or detecting changes.</summary>
    private static IUpdateEntry? TrackedNavigationEntry(
        IUpdateEntry owner,
        object entity,
        IEntityType entityType
    )
    {
        var contract = NestedSetInsertionContract.Instance;
        var entry = contract.FindTrackedEntry(contract.Manager(owner), entity, entityType);

        return entry is { EntityState: not EntityState.Detached } ? entry : null;
    }
}

namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal sealed partial class NestedSetRelationshipSnapshot
{

    /// <summary>Selects changed native identity or an explicit round trip back to the captured identity.</summary>
    private bool RequiresNativeRestore(
        ForeignKeyState state
    )
    {
        var changed = HasNativeIdentityChange(state, out var marked);

        return changed || (marked && HasSameCurrentIdentity(state));
    }

    /// <summary>Removes authorized dependents from one uncaptured wrong-target parent navigation.</summary>
    private void RestoreUnexpectedParent(
        IUpdateEntry entry,
        ParentDomain domain
    )
    {
        var navigation = domain.Navigation;

        if (_navigations.TryGetValue((entry, navigation), out var captured) && captured.IsAuthoritative)
        {
            return;
        }

        // WHY: Only these authorized dependents may be removed; unrelated pending members stay caller-owned.
        var current = entry.GetCurrentValue(navigation);

        if (navigation.IsCollection)
        {
            RestoreUnexpectedCollection(entry, domain, current);
        }
        else
        {
            RestoreUnexpectedReference(entry, domain, current);
        }
    }

    /// <summary>Compares native membership and installed snapshots before setters can hide pending intent.</summary>
    private bool HasNativeIdentityChange(
        ForeignKeyState state,
        out bool marked
    )
    {
        var nativeChanged = state.Entry.EntityState == EntityState.Detached
            || HasDependentMembership(state.Entry, state.Key, state.Values) != state.NativeMember;

        marked = false;

        // WHY: Native reference fixup can change an initially null reference without moving equal FK
        // scalars or an already absent dependent bucket. Its independent snapshot is evidence too.
        if (state.Key.DependentToPrincipal is { } reference
            && _navigations[(state.Entry, reference)].HasNativeReferenceChange)
        {
            nativeChanged = true;
        }

        // WHY: One differing component selects the whole FK, including shared/composite key effects.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < state.Values.Length; index++)
        {
            var property = state.Key.Properties[index];
            var conceptualNull = state.Entry.IsConceptualNull(property);
            marked |= state.Entry.IsModified(property);

            // WHY: Conceptual nulls remove dependent-map membership without changing the scalar snapshot.
            // Inspect every component before restoring scalars, including shared/composite required FKs.
            nativeChanged |= conceptualNull
                || !property
                    .GetKeyValueComparer()
                    .Equals(state.Entry.GetRelationshipSnapshotValue(property), state.Values[index]);
        }

        return nativeChanged;
    }

    /// <summary>Recognizes explicit EF round trips whose current identity again equals the captured one.</summary>
    private static bool HasSameCurrentIdentity(
        ForeignKeyState state
    )
    {
        var sameIdentity = true;

        // WHY: A marked EF round trip can end at its original native identity after moving list
        // positions. A raw write to a different FK must still retain its pending CLR membership.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < state.Values.Length; index++)
        {
            var property = state.Key.Properties[index];

            if (!property
                .GetKeyValueComparer()
                .Equals(state.Entry.GetCurrentValue(property), state.Values[index]))
            {
                sameIdentity = false;

                break;
            }
        }

        return sameIdentity;
    }

    /// <summary>Clears only selected current and snapshot references from an unexpected parent.</summary>
    private static void RestoreUnexpectedReference(
        IUpdateEntry entry,
        ParentDomain domain,
        object? current
    )
    {
        var navigation = domain.Navigation;

        if (current is not null
            && domain.Dependents.Contains(current))
        {
            NestedSetInsertionContract
                .Instance
                .Setter(navigation)
                .SetClrValueUsingContainingEntity(
                    entry.ToEntityEntry()
                        .Entity,
                    null);
        }

        if (entry.GetRelationshipSnapshotValue(navigation) is { } snapshot
            && domain.Dependents.Contains(snapshot))
        {
            NestedSetInsertionContract.Instance.SetRelationshipSnapshot(entry, navigation, null);
        }
    }

    /// <summary>Removes selected current and snapshot collection members while preserving unrelated order.</summary>
    private static void RestoreUnexpectedCollection(
        IUpdateEntry entry,
        ParentDomain domain,
        object? current
    )
    {
        var navigation = domain.Navigation;

        if (current is IList { IsReadOnly: false, IsFixedSize: false } list)
        {
            // WHY: Reverse removal preserves all unrelated positions and avoids notification Reset events.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = list.Count - 1; index >= 0; index--)
            {
                if (list[index] is { } member
                    && domain.Dependents.Contains(member))
                {
                    list.RemoveAt(index);
                }
            }
        }
        else if (current is IEnumerable values)
        {
            foreach (var member in values
                .Cast<object>()
                .Where(domain.Dependents.Contains)
                .ToArray())
            {
                navigation.GetCollectionAccessor()!.RemoveStandalone(current, member);
            }
        }

        if (entry.EntityType.GetChangeTrackingStrategy() == ChangeTrackingStrategy.Snapshot
            && entry.GetRelationshipSnapshotValue(navigation) is IEnumerable<object> original)
        {
            foreach (var member in original
                .Where(domain.Dependents.Contains)
                .ToArray())
            {
                NestedSetInsertionContract.Instance.RemoveCollectionSnapshot(entry, navigation, member);
            }
        }
    }
}

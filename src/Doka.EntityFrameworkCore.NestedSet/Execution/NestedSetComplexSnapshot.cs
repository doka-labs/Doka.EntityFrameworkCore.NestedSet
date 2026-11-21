namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Journals caller-owned complex containers separately from EF's current and original element lanes.</summary>
internal sealed partial class NestedSetComplexSnapshot
{
    private readonly List<CollectionSnapshot> _collections = [];
    private readonly EntityEntry _entry;
    private readonly List<MemberSnapshot> _members = [];
    private readonly object _owner;

    /// <summary>Captures complex topology without detecting raw changes or replacing caller instances.</summary>
    /// <param name="entry">The entity whose complex graph shares the coordinated write.</param>
    /// <param name="owner">The same entity's existing native update identity.</param>
    internal NestedSetComplexSnapshot(
        EntityEntry entry,
        IUpdateEntry owner
    )
    {
        _entry = entry;
        _owner = owner;
        CaptureMembers(entry.Metadata, entry.Entity, []);
        CaptureCollections(owner, entry.Metadata, entry.Entity, []);
    }

    /// <summary>Reinstates containers and native positions before any captured scalar wrapper is read.</summary>
    internal void Restore()
    {
        if (_collections.All(collection => collection.MatchesLanes()))
        {
            // WHY: Raw callback edits leave native identities unchanged; only independent originals need replay.
            RestoreMembers();
            RestoreOriginalLanes();

            return;
        }

        var captured = CaptureRetainedEntries();
        RestoreMembers();

        try
        {
            RetireCollectionLanes(captured);
            RestoreRetainedLanes(captured);
            RestoreHiddenLanes();
        }
        finally
        {
            UndoStaging();
            RestoreMembers();
            RestoreCollectionCapacity();
        }
    }

    /// <summary>Replays the exact CLR preimage after native or scalar setters have visited containing values.</summary>
    internal void RestoreMembers()
    {
        // WHY: Visit the stable, operation-owned CLR member journal in capture order.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var memberIndex = 0; memberIndex < _members.Count; memberIndex++)
        {
            var member = _members[memberIndex];

            member.Restore(_entry.Entity);
        }
    }

    /// <summary>Replays hidden scalars after a container replay put an old struct box back.</summary>
    internal void RestoreHiddenValues()
    {
        // WHY: Index the stable, operation-owned collection frames in capture order.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var collectionIndex = 0; collectionIndex < _collections.Count; collectionIndex++)
        {
            var collection = _collections[collectionIndex];

            collection.RestoreHiddenValues();
        }
    }

    /// <summary>Restores aggregate intent after scalar and entity state transitions have settled.</summary>
    internal void RestoreFlags()
    {
        // WHY: Aggregate notifications propagate to containing entries. Child flags settle before parents;
        // the native non-recursive setter preserves independently captured Added and Deleted element states.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = _collections.Count - 1; index >= 0; index--)
        {
            _collections[index]
                .RestoreFlag();
        }
    }

    /// <summary>Rejects incomplete graph recovery before the context can be reused.</summary>
    internal void Verify()
    {
        // WHY: Visit the stable, operation-owned CLR member journal in capture order.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var memberIndex = 0; memberIndex < _members.Count; memberIndex++)
        {
            var member = _members[memberIndex];

            member.Verify(_entry.Entity);
        }

        // WHY: Index the stable, operation-owned collection frames in capture order.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var collectionIndex = 0; collectionIndex < _collections.Count; collectionIndex++)
        {
            var collection = _collections[collectionIndex];

            collection.Verify();
        }
    }
}

namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal sealed partial class NestedSetComplexSnapshot
{
    /// <summary>Indexes retained identities and captures live references before CLR preimages hide them.</summary>
    private HashSet<object> CaptureRetainedEntries()
    {
        var captured = new HashSet<object>(ReferenceEqualityComparer.Instance) { _owner };

        // WHY: The captured collection journal is operation-owned and retains its order during recovery.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < _collections.Count; index++)
        {
            foreach (var element in _collections[index].Elements)
            {
                captured.Add(element.Native);
            }
        }

        // WHY: The captured collection journal is operation-owned and retains its order during recovery.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < _collections.Count; index++)
        {
            _collections[index].CaptureLiveReferences(_entry.Entity);
        }

        return captured;
    }

    /// <summary>Retires callback-shifted lanes before their captured ordinals are installed again.</summary>
    private void RetireCollectionLanes(
        HashSet<object> captured
    )
    {
        // WHY: Retirement and reinstallation need separate access graphs for their different ordinal paths.
        StageCollections(retiring: true);
        DetachCollections(
            _owner,
            _entry.Metadata,
            new HashSet<object>(ReferenceEqualityComparer.Instance),
            captured);

        // WHY: The captured collection journal is operation-owned and retains its order during recovery.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < _collections.Count; index++)
        {
            _collections[index].DetachRetained(captured);
        }

        UndoStaging();

        // WHY: The captured collection journal is operation-owned and retains its order during recovery.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < _collections.Count; index++)
        {
            _collections[index].RestoreOrdinals();
        }
    }

    /// <summary>Reinstalls captured entries while preserving the lifecycle needed for retained deleted slots.</summary>
    private void RestoreRetainedLanes(
        HashSet<object> captured
    )
    {
        var holdsEntity = PrepareDeletedHolds();
        StageCollections(retiring: false);

        // WHY: Containing transitions initialize children; restore independent originals before local replay.
        RestoreOriginalLanes();
        var entityState = NestedSetComplexEntryAccess.State(_owner);

        if (holdsEntity)
        {
            HoldEntityAsDeleted();
        }

        // WHY: The captured collection journal is operation-owned and retains its order during recovery.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < _collections.Count; index++)
        {
            _collections[index].Restore(captured);
        }

        ReleaseDeletedHolds(holdsEntity, entityState);
    }

    /// <summary>Restores hidden scalar intent before releasing temporary element addresses.</summary>
    private void RestoreHiddenLanes()
    {
        // WHY: The captured collection journal is operation-owned and retains its order during recovery.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < _collections.Count; index++)
        {
            _collections[index].RestoreHiddenProperties();
        }

        // WHY: The captured collection journal is operation-owned and retains its order during recovery.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < _collections.Count; index++)
        {
            _collections[index].ReleaseAddresses();
        }
    }

    /// <summary>Replays independent original buffers without changing current CLR membership.</summary>
    private void RestoreOriginalLanes()
    {
        // WHY: The captured collection journal is operation-owned and retains its order during recovery.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < _collections.Count; index++)
        {
            _collections[index].RestoreOriginal();
        }
    }

    /// <summary>Builds the temporary access graph for one retirement or reinstallation phase.</summary>
    private void StageCollections(
        bool retiring
    )
    {
        // WHY: The captured collection journal is operation-owned and retains its order during recovery.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < _collections.Count; index++)
        {
            _collections[index].Stage(_entry.Entity, retiring);
        }
    }

    /// <summary>Trims only the native capacity grown by temporary access and lifecycle callbacks.</summary>
    private void RestoreCollectionCapacity()
    {
        // WHY: The final lane lengths include legitimate captured trailing null ordinal slots.
        // WHY: The captured collection journal is operation-owned and retains its order during recovery.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < _collections.Count; index++)
        {
            _collections[index].RestoreCapacity();
        }
    }

}

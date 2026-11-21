namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>
///     Preserves pending tracked values until a coordinated or managed save has completed its transaction.
/// </summary>
internal sealed partial class NestedSetTrackerSnapshot
{
    private static readonly ConditionalWeakTable<IEntityType, IProperty[]> s_properties = new();
    private readonly DbContext _context;
    private readonly EntrySnapshot[] _entries;
    private readonly NestedSetRelationshipSnapshot? _relationships;
    private List<(IUpdateEntry Owner, INavigation Navigation, object Member)>? _persistedAdditions;

    /// <summary>Retains the original entry objects so rollback does not replace the caller's tracked graph.</summary>
    /// <param name="context">The context that owns the captured entries.</param>
    /// <param name="entries">The pending tracked state captured before its save sends any command.</param>
    /// <param name="relationships">The affected native relationships and caller graph, when pending FKs exist.</param>
    private NestedSetTrackerSnapshot(
        DbContext context,
        EntrySnapshot[] entries,
        NestedSetRelationshipSnapshot? relationships
    )
    {
        _context = context;
        _entries = entries;
        _relationships = relationships;
    }

    /// <summary>Captures all tracked entities after change detection and before saving without acceptance.</summary>
    /// <param name="context">The context whose hierarchy and unrelated pending changes share the save.</param>
    /// <returns>A snapshot that can restore captured values after the database transaction is rolled back.</returns>
    /// <remarks>
    ///     Complex value callbacks are journaled separately from entity membership and structural guards.
    /// </remarks>
    internal static NestedSetTrackerSnapshot Capture(
        DbContext context
    )
    {
        // WHY: The public update adapter exposes conceptual nulls that ordinary CLR values cannot represent.
        // Create() retains this context's entries; a standalone adapter would describe a different tracker.
        var updates = context
            .GetService<IUpdateAdapterFactory>()
            .Create();

        return Capture(context, updates.Entries);
    }

    /// <summary>Captures only the pending entries that one library-owned save is about to persist.</summary>
    /// <param name="context">The context that owns the entries.</param>
    /// <param name="entries">The pending update entries passed to Entity Framework Core's persistence step.</param>
    /// <returns>A snapshot that can restore the entries after a later rollback, even when EF accepted them.</returns>
    internal static NestedSetTrackerSnapshot Capture(
        DbContext context,
        IEnumerable<IUpdateEntry> entries
    )
    {
        var tracker = context.ChangeTracker;
        var autoDetectChanges = tracker.AutoDetectChangesEnabled;
        tracker.AutoDetectChangesEnabled = false;

        try
        {
            NestedSetRelationshipSnapshot? relationships = null;
            var snapshots = entries
                .Select(entry =>
                {
                    NestedSetRelationshipSnapshot.Capture(context, entry, ref relationships);

                    return new EntrySnapshot(entry);
                })
                .ToArray();

            relationships?.CaptureParentNavigations();

            return new NestedSetTrackerSnapshot(context, snapshots, relationships);
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = autoDetectChanges;
        }
    }

    /// <summary>Captures proven writes owned by the complete coordinated transaction.</summary>
    /// <param name="entries">The exact provider write set after application callbacks.</param>
    /// <returns>Work to install only successfully persisted collection relationships.</returns>
    internal Action? CapturePersistedRelationships(
        IList<IUpdateEntry> entries
    ) => NestedSetCollectionBaseline.CapturePersisted(entries, this);

    /// <summary>Owns only a native membership that the library is about to install after provider success.</summary>
    /// <param name="owner">The exact tracked owner resolved from a proven persisted FK.</param>
    /// <param name="navigation">The inverse collection whose native set did not contain the member.</param>
    /// <param name="member">The saved reference present in the prepared CLR collection.</param>
    internal void RecordPersistedAddition(
        IUpdateEntry owner,
        INavigation navigation,
        object member
    ) => (_persistedAdditions ??= []).Add((owner, navigation, member));

    /// <summary>
    ///     Reverses owned native additions without replaying a callback's unrelated CLR or snapshot state.
    /// </summary>
    private void UndoPersistedAdditions()
    {
        if (_persistedAdditions is null)
        {
            return;
        }

        // WHY: Only captured native memberships are removed; this private receipt list stays stable.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var additionIndex = 0; additionIndex < _persistedAdditions.Count; additionIndex++)
        {
            var (owner, navigation, member) = _persistedAdditions[additionIndex];

            // WHY: EF fixup may already have removed the member or its slot. Do not recreate an empty sidecar
            // or change any CLR collection while undoing only this library's successful native addition.
            if (owner.GetRelationshipSnapshotValue(navigation) is ISet<object> members
                && members.Contains(member))
            {
                NestedSetInsertionContract.Instance.RemoveCollectionSnapshot(owner, navigation, member);
            }
        }
    }

    /// <summary>Restores pending state after rollback without clearing or detaching the tracked graph.</summary>
    /// <exception cref="InvalidOperationException">A captured value or state could not be restored.</exception>
    internal void Restore()
    {
        var tracker = _context.ChangeTracker;
        var autoDetectChanges = tracker.AutoDetectChangesEnabled;
        var cascadeDeleteTiming = tracker.CascadeDeleteTiming;
        var deleteOrphansTiming = tracker.DeleteOrphansTiming;
        tracker.AutoDetectChangesEnabled = false;
        tracker.CascadeDeleteTiming = CascadeTiming.Never;
        tracker.DeleteOrphansTiming = CascadeTiming.Never;

        try
        {
            _relationships?.PrepareRestore();

            RestoreEntryState();

            _relationships?.RestoreForeignKeys();
            _relationships?.RestoreNavigations();

            RestoreEntryIntent();

            _relationships?.RestorePendingBuckets();

            RestoreAuthoritativeRelationships();
        }
        finally
        {
            tracker.CascadeDeleteTiming = cascadeDeleteTiming;
            tracker.DeleteOrphansTiming = deleteOrphansTiming;
            tracker.AutoDetectChangesEnabled = autoDetectChanges;
        }
    }

    /// <summary>Clears provider-induced aggregate flags only inside an originally Unchanged complex graph.</summary>
    /// <param name="references">The direct complex references whose nested collections may retain flags.</param>
    /// <param name="collections">The direct complex collections belonging to the current containing entry.</param>
    private static void ClearUnmodifiedComplexCollections(
        IEnumerable<ComplexPropertyEntry> references,
        IEnumerable<ComplexCollectionEntry> collections
    )
    {
        foreach (var reference in references)
        {
            if (reference.CurrentValue is not null)
            {
                ClearUnmodifiedComplexCollections(reference.ComplexProperties, reference.ComplexCollections);
            }
        }

        foreach (var collection in collections)
        {
            if (collection.CurrentValue is IList values)
            {
                // WHY: Settle nested aggregates before their containing collection. The public aggregate
                // setter visits flattened elements; originally Modified graphs never enter this clearing path.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var index = 0; index < values.Count; index++)
                {
                    if (values[index] is not null)
                    {
                        var element = collection[index];

                        // WHY: EF's ComplexElementEntry.ComplexProperties also enumerates collections.
                        // Filter metadata before constructing reference entries, since wrapping a collection throws.
                        ClearUnmodifiedComplexCollections(
                            element
                                .Metadata
                                .ComplexType
                                .GetComplexProperties()
                                .Where(property => !property.IsCollection)
                                .Select(element.ComplexProperty),
                            element.ComplexCollections);
                    }
                }
            }

            if (collection.IsModified)
            {
                collection.IsModified = false;
            }
        }
    }

    /// <summary>Writes a mapped CLR field, property or indexer without changing tracker state or flags.</summary>
    internal static void SetMappedClrValue(
        object instance,
        IPropertyBase metadata,
        object? value
    )
    {
        var member = metadata.GetMemberInfo(forMaterialization: false, forSet: true);

        switch (member)
        {
            case FieldInfo field:
                field.SetValue(instance, value);

                break;
            case PropertyInfo property when metadata.IsIndexerProperty():
                property.SetValue(instance, value, [metadata.Name]);

                break;
            case PropertyInfo property:
                property.SetValue(instance, value);

                break;
            default:
                throw new InvalidOperationException("The mapped CLR member cannot be restored after rollback.");
        }
    }
}

namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal sealed partial class NestedSetCollectionBaseline
{
    /// <summary>Captures ordinary FK writes before commands or completion callbacks can alter their values.</summary>
    /// <param name="entries">The exact validated provider write set, without another detection pass.</param>
    /// <returns>Completion work only when the exact write set contains a proven collection relationship.</returns>
    internal static Action? CapturePersisted(
        IList<IUpdateEntry> entries
    ) => CapturePersisted(entries, null);

    /// <summary>Shares the coordinated rollback owner when a proven saved baseline needs installation.</summary>
    /// <param name="entries">The exact validated provider write set.</param>
    /// <param name="rollback">The outer coordinated snapshot, absent for ordinary EF saves.</param>
    /// <returns>Completion work restricted to relationships actually written by this attempt.</returns>
    internal static Action? CapturePersisted(
        IList<IUpdateEntry> entries,
        NestedSetTrackerSnapshot? rollback
    )
    {
        List<PendingRelationship>? pending = null;

        // WHY: The already validated provider write list stays stable throughout this capture pass.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];

            if (entry.EntityState != EntityState.Modified
                || entry.SharedIdentityEntry is not null
                || !NestedSetModelMapping.For(entry.EntityType.Model).TryDescriptor(entry.EntityType, out _))
            {
                continue;
            }

            var keys = s_keys.GetValue(
                entry.EntityType,
                static type => type.GetForeignKeys()
                    .Where(key => key is { IsOwnership: false, PrincipalToDependent.IsCollection: true })
                    .ToArray());

            foreach (var key in keys)
            {
                if (CapturePersistedRelationship(entry, key) is { } relationship)
                {
                    (pending ??= []).Add(relationship);
                }
            }
        }

        return pending is null ? null : () => CompletePersisted(pending, rollback);
    }

    /// <summary>Requires an actual written member without capturing the outer loop's native entry.</summary>
    /// <param name="entry">The eligible modified hierarchy entry.</param>
    /// <param name="foreignKey">The ordinary FK whose members may be written.</param>
    /// <returns>Whether a member is provider-generated or explicitly included in the update.</returns>
    private static bool HasWrittenForeignKey(
        IUpdateEntry entry,
        IForeignKey foreignKey
    )
    {
        // WHY: A capturing predicate hoisted the outer entry before its early skip, costing 32 bytes per row.
        // Index the metadata list without another predicate or interface-enumerator allocation.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < foreignKey.Properties.Count; index++)
        {
            var property = foreignKey.Properties[index];

            if (entry.IsStoreGenerated(property)
                || (entry.IsModified(property) && property.GetAfterSaveBehavior() == PropertySaveBehavior.Save))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Installs only successfully written vectors, including actual provider-generated FK values.</summary>
    private static void CompletePersisted(
        List<PendingRelationship> pending,
        NestedSetTrackerSnapshot? rollback
    )
    {
        // WHY: Complete each stable pending relationship receipt exactly once.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var relationshipIndex = 0; relationshipIndex < pending.Count; relationshipIndex++)
        {
            var relationship = pending[relationshipIndex];

            var proven = true;

            foreach (var index in relationship.ReadBack)
            {
                var property = relationship.ForeignKey.Properties[index];

                if (!relationship.Entry.HasStoreGeneratedValue(property))
                {
                    proven = false;

                    break;
                }

                relationship.Values[index] = NestedSetStructuralValue.Snapshot(
                    property,
                    relationship.Entry.GetCurrentValue(property));
            }

            if (proven)
            {
                RecordSaved(
                    relationship.Entry,
                    relationship.Entry.ToEntityEntry()
                        .Entity,
                    relationship.ForeignKey,
                    relationship.Values,
                    rollback);
            }
        }
    }

    /// <summary>Retains only one write set's exact FK values until the provider succeeds or throws.</summary>
    private sealed record PendingRelationship(
        IUpdateEntry Entry,
        IForeignKey ForeignKey,
        object?[] Values,
        int[] ReadBack
    );

    /// <summary>Retains only a complete FK vector proven to be consumed or read back by this write.</summary>
    private static PendingRelationship? CapturePersistedRelationship(
        IUpdateEntry entry,
        IForeignKey key
    )
    {
        if (!HasWrittenForeignKey(entry, key))
        {
            return null;
        }

        if (entry.EntityType.GetUpdateStoredProcedureMappings().Any() && !key.Properties.All(entry.IsStoreGenerated))
        {
            // WHY: Procedures can consume originals or compute values; flags alone cannot prove their stored FK.
            return null;
        }

        var values = new object?[key.Properties.Count];
        List<int>? readBack = null;

        // WHY: Keep scalar components paired with their immutable persisted vector and generated readback slots.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < values.Length; index++)
        {
            var property = key.Properties[index];

            if (!TryCapturePersistedValue(entry, property, out var value, out var generated))
            {
                return null;
            }

            if (generated)
            {
                (readBack ??= []).Add(index);
            }
            else
            {
                values[index] = NestedSetStructuralValue.Snapshot(property, value);
            }
        }

        return new PendingRelationship(entry, key, values, readBack?.ToArray() ?? []);
    }

    /// <summary>Separates written values, generated readback and unproven shared-column payload.</summary>
    private static bool TryCapturePersistedValue(
        IUpdateEntry entry,
        IProperty property,
        out object? value,
        out bool generated
    )
    {
        value = null;
        generated = entry.IsStoreGenerated(property);

        if (generated)
        {
            return true;
        }

        if (entry.IsModified(property) && property.GetAfterSaveBehavior() == PropertySaveBehavior.Save)
        {
            // WHY: An inserted principal can propagate its real generated key into this written FK later.
            generated = entry.HasTemporaryValue(property);
            value = generated ? null : entry.GetCurrentValue(property);

            return true;
        }

        if (entry.CanHaveOriginalValue(property)
            && property.GetTableColumnMappings()
                .All(mapping => mapping.Column.PropertyMappings.All(other => other.Property == property)))
        {
            value = entry.GetOriginalValue(property);

            return true;
        }

        // WHY: An aliased unmarked column may be written by another property; originals are not store proof.
        return false;
    }
}

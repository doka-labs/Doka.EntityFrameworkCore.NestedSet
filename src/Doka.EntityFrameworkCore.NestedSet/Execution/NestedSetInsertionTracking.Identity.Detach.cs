using System.Runtime.ExceptionServices;

namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal static partial class NestedSetInsertionTracking
{
    internal sealed partial class Identity
    {
        /// <summary>Restores key representations before ordinary detachment can perform relationship fixup.</summary>
        /// <remarks>Prepare every introduced root and dependent before detaching any member of its aggregate.</remarks>
        internal void PrepareDetach()
        {
            List<Exception>? errors = null;

            CaptureAdditionalIdentities(ref errors);

            CaptureDependentBuckets();

            RestoreInstalledValues(ref errors);

            if (errors is not null)
            {
                throw new AggregateException("Insertion identity restoration failed. Discard the context.", errors);
            }
        }

        /// <summary>Retains an exact additional native slot when its key differs from the staged identity.</summary>
        private void CaptureAdditionalKey(
            IKey key,
            object?[] staged,
            bool relationship
        )
        {
            var differs = false;
            var shape = s_shapes.GetValue(key, CreateShape);

            // WHY: Compare typed readers against the matching staged key slots without a projection iterator.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < key.Properties.Count; index++)
            {
                var reader = shape.Readers[index];
                var matches = relationship
                    ? reader.MatchesRelationship(UpdateEntry, staged[index])
                    : reader.MatchesCurrent(UpdateEntry, staged[index]);

                if (!matches)
                {
                    differs = true;
                    break;
                }
            }

            if (!differs)
            {
                return;
            }

            var values = new object?[key.Properties.Count];

            // WHY: Fill native key-vector slots directly from the same property ordinals.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = NestedSetStructuralValue.Snapshot(
                    key.Properties[index],
                    ReadKeyValue(key.Properties[index], relationship));
            }

            var map = Contract.FindMap(_manager, key);

            if (map is not null
                && ReferenceEquals(
                    Contract
                        .Map(map.GetType())
                        .Find(map, values),
                    UpdateEntry))
            {
                (_additionalKeys ??= []).Add((key, values));
            }
        }

        /// <summary>Reads one known key component without allocating a callback delegate inside the row loop.</summary>
        private object? ReadKeyValue(
            IProperty property,
            bool relationship
        ) => relationship
            ? UpdateEntry.GetRelationshipSnapshotValue(property)
            : UpdateEntry.GetCurrentValue(property);

        /// <summary>Runs ordinary detachment, then removes this entry's exact surviving map memberships.</summary>
        internal void Detach()
        {
            Exception? detachError = null;

            try
            {
                NestedSetInsertionTracking.Detach(_entry);
            }
            catch (Exception error)
            {
                detachError = error;
            }

            List<Exception>? errors = null;

            if (_entry.State == EntityState.Detached)
            {
                RemovePrimaryIdentities(ref errors);
                RemoveDependentMemberships(ref errors);
                RemoveAdditionalIdentities(ref errors);
            }

            if (errors is not null)
            {
                if (detachError is not null)
                {
                    errors.Insert(0, detachError);
                }

                throw new AggregateException("Insertion identity cleanup failed. Discard the context.", errors);
            }

            if (detachError is not null)
            {
                ExceptionDispatchInfo
                    .Capture(detachError)
                    .Throw();
            }
        }

        /// <summary>Retains only exact existing foreign-key buckets that contain this introduced dependent.</summary>
        private void CaptureDependentBuckets()
        {
            if (_foreignKeys.Length == 0)
            {
                return;
            }

            var primaryKey = _entry.Metadata.FindPrimaryKey();
            var map = primaryKey is null
                ? null
                : Contract.FindMap(_manager, primaryKey);

            if (map is null)
            {
                return;
            }

            var operations = Contract.Map(map.GetType());

            // WHY: Keep each foreign key aligned with its reusable dependent-value vector.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < _foreignKeys.Length; index++)
            {
                CaptureDependentBucket(operations, map, index);
            }
        }
    }
}

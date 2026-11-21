namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal static partial class NestedSetInsertionTracking
{
    internal sealed partial class Identity
    {
        /// <summary>Captures the exact existing native bucket of one complete FK vector.</summary>
        private void CaptureDependentBucket(
            NestedSetInsertionContract.KeyMap operations,
            object map,
            int index
        )
        {
            var foreignKey = _foreignKeys[index];
            var dependentMap = operations.Dependents(map, foreignKey);

            if (dependentMap is null
                && (foreignKey.IsOwnership || foreignKey.PrincipalToDependent is not { IsCollection: true }))
            {
                return;
            }

            // WHY: Allocate vectors only for existing native buckets or a collection's saved-reference proof.
            var values = _foreignKeyValues[index] ??= new object?[foreignKey.Properties.Count];

            var valid = RefreshDependentValues(foreignKey, values);

            if (!valid)
            {
                // WHY: Native dependent factories omit absent optional links. Their direct key-values
                // lookup requires a complete non-null key, so an absent parent owns no bucket to capture.
                return;
            }

            if (dependentMap is null)
            {
                // WHY: A prepared collection can skip EF's AddToCollection and never create a dependent
                // bucket. Its persisted FK vector is still needed to baseline that exact saved reference.
                return;
            }

            var entries = Contract.Dependents(dependentMap, values);

            if (entries is ICollection<IUpdateEntry> bucket
                && bucket.Contains(UpdateEntry))
            {
                // WHY: Most roots have no dependent bucket; allocate storage only for an actual membership.
                var buckets = _buckets ??= [];

                if (!buckets.Contains(bucket))
                {
                    buckets.Add(bucket);
                }
            }
        }

        /// <summary>Reuses unchanged typed values while refreshing only changed FK components.</summary>
        private bool RefreshDependentValues(
            IForeignKey foreignKey,
            object?[] values
        )
        {
            var absent = false;

            // WHY: Match each FK component to its existing vector slot without building a projected sequence.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var propertyIndex = 0; propertyIndex < values.Length; propertyIndex++)
            {
                var property = foreignKey.Properties[propertyIndex];
                var reader = s_readers.GetValue(property, CreateReader);

                if (!reader.MatchesCurrent(UpdateEntry, values[propertyIndex]))
                {
                    // WHY: EF's native dependent lookup accepts object vectors. Only initial or changed
                    // scalar FK components cross that boundary; unchanged components reuse their boxes.
                    values[propertyIndex] = NestedSetStructuralValue.Snapshot(
                        property,
                        UpdateEntry.GetCurrentValue(property));
                }

                absent |= values[propertyIndex] is null || UpdateEntry.IsConceptualNull(property);
            }

            return !absent;
        }
    }
}

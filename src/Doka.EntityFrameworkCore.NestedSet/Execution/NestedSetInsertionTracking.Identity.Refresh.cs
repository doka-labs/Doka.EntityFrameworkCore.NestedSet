namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal static partial class NestedSetInsertionTracking
{
    internal sealed partial class Identity
    {
        /// <summary>Captures one changed key and reinstalls only its exact mutable native identity.</summary>
        private void RefreshKey(
            int index,
            IKey key
        )
        {
            var (values, clrValues, mutable) = CaptureKeyValues(key);

            var map = Contract.FindMap(_manager, key);

            if (map is not null)
            {
                var operations = Contract.Map(map.GetType());

                if (_values[index] is { } previous)
                {
                    if (!ReferenceEquals(operations.Find(map, values), UpdateEntry))
                    {
                        // WHY: A later Tracking callback may change an earlier root before the final
                        // capture. That current representation never became an installed identity;
                        // retain the exact old snapshot and dependent buckets for rejection cleanup.
                        return;
                    }

                    if (!SameKey(key, previous, values))
                    {
                        operations.Remove(map, previous, UpdateEntry);
                    }
                }

                if (mutable)
                {
                    // WHY: EF installs live mutable key objects into a hash map. A callback can mutate their
                    // stored hash without replacing the key. Install the comparer snapshot before callbacks;
                    // native operations still use the exact entry and preserve unrelated tracked identities.
                    operations.RemoveCurrent(map, UpdateEntry);
                    operations.Add(map, values, UpdateEntry);
                }
            }

            _values[index] = values;
            _clrValues[index] = clrValues;
        }
        /// <summary>Snapshots one changed key's native and CLR values using exact comparer semantics.</summary>
        private (object?[] Values, object?[] ClrValues, bool Mutable) CaptureKeyValues(
            IKey key
        )
        {
            var values = new object?[key.Properties.Count];
            var clrValues = new object?[key.Properties.Count];
            var mutable = false;

            // WHY: Matching ordinals keep property metadata and both snapshot vectors aligned.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var propertyIndex = 0; propertyIndex < values.Length; propertyIndex++)
            {
                var property = key.Properties[propertyIndex];
                var current = UpdateEntry.GetCurrentValue(property);
                var snapshot = NestedSetStructuralValue.Snapshot(property, current);
                values[propertyIndex] = snapshot;
                clrValues[propertyIndex] = property.IsShadowProperty()
                    ? null
                    : NestedSetStructuralValue.Snapshot(
                        property,
                        property
                            .GetGetter()
                            .GetClrValueUsingContainingEntity(_entry.Entity));

                mutable |= current is not null
                    && !property.ClrType.IsValueType
                    && !ReferenceEquals(current, snapshot);
            }

            return (values, clrValues, mutable);
        }

    }
}

namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal sealed partial class NestedSetTrackerSnapshot
{
    private sealed partial class EntrySnapshot
    {
        /// <summary>Pairs scalar snapshots with the exact native entry at each retained collection ordinal.</summary>
        private void CaptureComplexCollection(
            IComplexProperty property,
            ComplexCollectionEntry collection,
            List<PropertySnapshot> target,
            List<int> ordinals,
            bool hasCurrentPath
        )
        {
            var deleted = NestedSetComplexEntryAccess.State(NestedSetComplexEntryAccess.Owner(collection))
                == EntityState.Deleted;

            var value = deleted
                ? NestedSetComplexEntryAccess.Original(NestedSetComplexEntryAccess.Owner(collection), property)
                : collection.CurrentValue;

            if (value is not IList values)
            {
                return;
            }

            var elementComplexProperties = property.ComplexType.GetComplexProperties();

            // WHY: EF metadata is repeatable. Probe once to avoid empty recursion, then reuse it per element
            // without allocating a metadata array.
            // ReSharper disable once PossibleMultipleEnumeration
            var hasNestedProperties = elementComplexProperties.Any();

            // WHY: Pair each complex collection value with the EF element entry at the same position.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < values.Count; index++)
            {
                if (values[index] is null)
                {
                    continue;
                }

                var element = deleted
                    ? collection.GetOriginalEntry(index)
                    : collection[index];

                var instance = values[index];
                var currentPath = hasCurrentPath && !deleted;

                CaptureProperties(element.Properties, instance, target);

                if (hasNestedProperties)
                {
                    // WHY: Leaf scalars are already captured. Creating two bound member delegates
                    // for an empty recursive visit adds allocation without retaining any more state.
                    ordinals.Add(index);
                    CaptureComplexValues(

                        // ReSharper disable once PossibleMultipleEnumeration
                        elementComplexProperties,
                        element.ComplexProperty,
                        element.ComplexCollection,
                        target,
                        ordinals,
                        instance,
                        currentPath);

                    ordinals.RemoveAt(ordinals.Count - 1);
                }
            }
        }

        /// <summary>Captures retained reference storage before recursively visiting its complex values.</summary>
        private void CaptureComplexReference(
            IComplexProperty property,
            ComplexPropertyEntry reference,
            List<PropertySnapshot> target,
            List<int> ordinals,
            object? container,
            bool hasCurrentPath
        )
        {
            // WHY: A public CurrentValue beneath a Deleted owner reads an original the reference does not
            // track. The retained container is that owner's actual CLR storage in every lifecycle state.
            var owner = container is ComplexClrLocation location
                ? location.Captured
                : container;

            var referenceValue = owner is null
                ? null
                : property
                    .GetGetter()
                    .GetClrValue(owner);

            var referenceLocation = referenceValue is not null && property.ComplexType.ClrType.IsValueType
                ? new ComplexClrLocation(
                    referenceValue,
                    _entry.Entity,
                    ordinals.ToArray(),
                    hasCurrentPath,
                    container,
                    property)
                : referenceValue;

            CaptureProperties(reference.Properties, referenceLocation, target);
            var referenceComplexProperties = property.ComplexType.GetComplexProperties();

            // WHY: EF metadata is repeatable. The probe avoids empty recursion and bound delegates without
            // allocating an array solely to enumerate the same metadata again.
            // ReSharper disable once PossibleMultipleEnumeration
            if (referenceComplexProperties.Any())
            {
                // WHY: A scalar-only reference needs no recursive accessor delegates or ordinal work.
                CaptureComplexValues(

                    // ReSharper disable once PossibleMultipleEnumeration
                    referenceComplexProperties,
                    reference.ComplexProperty,
                    reference.ComplexCollection,
                    target,
                    ordinals,
                    referenceLocation,
                    hasCurrentPath);
            }
        }
    }
}

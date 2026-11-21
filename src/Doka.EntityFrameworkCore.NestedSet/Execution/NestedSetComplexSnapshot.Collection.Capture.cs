namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal sealed partial class NestedSetComplexSnapshot
{
    private sealed partial class CollectionSnapshot
    {
        /// <summary>Retains absent current reference owners through their original CLR instances.</summary>
        private static List<(IComplexProperty Property, object Instance)>? CaptureOriginalReferences(
            object owner,
            IComplexProperty property,
            IList? currentValues
        )
        {
            List<(IComplexProperty Property, object Instance)>? references = null;
            var type = property.DeclaringType;

            while (currentValues is null
                && type is IComplexType { ComplexProperty.IsCollection: false } complex)
            {
                if (complex.ComplexProperty.IsNullable
                    && NestedSetComplexEntryAccess.Original(owner, complex.ComplexProperty) is { } reference)
                {
                    (references ??= []).Add((complex.ComplexProperty, reference));
                }

                type = complex.ComplexProperty.DeclaringType;
            }

            references?.Reverse();

            return references;
        }

        /// <summary>Uses aligned lanes directly or captures the exact union of independent native identities.</summary>
        private (ElementSnapshot[] Elements, int CurrentCount, int OriginalCount) CaptureElements(
            IList current,
            IList original
        )
        {
            if (TryCaptureAligned(current, original, out var aligned))
            {
                return (aligned, aligned.Length, aligned.Length);
            }

            var elements = new Dictionary<object, ElementSnapshot>(ReferenceEqualityComparer.Instance);
            var currentCount = CaptureCurrentElements(current, elements);
            var originalCount = CaptureOriginalElements(original, elements);

            return (elements.Values.ToArray(), currentCount, originalCount);
        }

        /// <summary>Captures current native slots, including Added and Deleted minus-one ordinals.</summary>
        private int CaptureCurrentElements(
            IList current,
            Dictionary<object, ElementSnapshot> elements
        )
        {
            var count = 0;

            // WHY: Each native slot must remain paired with its actual current CLR access position.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < current.Count; index++)
            {
                if (current[index] is { } native)
                {
                    count++;
                    elements.Add(native, new ElementSnapshot(native, index, CurrentItem(index) is not null));
                }
            }

            return count;
        }

        /// <summary>Includes original-only Deleted entries without inventing a current CLR access slot.</summary>
        private static int CaptureOriginalElements(
            IList original,
            Dictionary<object, ElementSnapshot> elements
        )
        {
            var count = 0;

            // WHY: Original slots are independent of current ordinals and may hold the only retained identity.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < original.Count; index++)
            {
                if (original[index] is { } native)
                {
                    count++;

                    if (!elements.TryGetValue(native, out var element))
                    {
                        element = new ElementSnapshot(native, -1, false);
                        elements.Add(native, element);
                    }

                    element.OriginalSlot = index;
                }
            }

            return count;
        }

        /// <summary>Captures scalars whose native entries are hidden below Deleted or missing CLR owners.</summary>
        private List<HiddenPropertySnapshot>? CaptureHiddenProperties()
        {
            // WHY: EF exposes no public entry for a current element below a Deleted owner, so the scalar journal
            // cannot pair it. Its intent and CLR values are retained through this native path instead.
            List<HiddenPropertySnapshot>? hidden = null;
            var deletedOwner = NestedSetComplexEntryAccess.State(Owner) == EntityState.Deleted;

            foreach (var element in Elements)
            {
                if (!deletedOwner
                    && CurrentItem(element.CurrentSlot) is not null)
                {
                    continue;
                }

                var originalInstance = CurrentItem(element.CurrentSlot)
                    ?? (_original is IList originals && (uint)element.OriginalSlot < (uint)originals.Count
                        ? originals[element.OriginalSlot]
                        : null);

                foreach (var scalar in _property.ComplexType.GetFlattenedProperties())
                {
                    (hidden ??= []).Add(
                        new HiddenPropertySnapshot(
                            element.Native,
                            scalar,
                            originalInstance,
                            element.CurrentSlot >= 0 && _path.All(ordinal => ordinal >= 0)));
                }
            }

            return hidden;
        }
    }
}

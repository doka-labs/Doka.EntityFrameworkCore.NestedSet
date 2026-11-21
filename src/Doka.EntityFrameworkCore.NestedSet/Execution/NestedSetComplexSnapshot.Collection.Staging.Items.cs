namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal sealed partial class NestedSetComplexSnapshot
{
    private sealed partial class CollectionSnapshot
    {
        /// <summary>Includes every captured, CLR and native lane extent before borrowing temporary slots.</summary>
        private int StagingCount(
            IList? previous
        )
        {
            var count = Math.Max(_currentCount, _originalCount);
            count = Math.Max(count, _items?.Length ?? 0);
            count = Math.Max(count, previous?.Count ?? 0);
            count = Math.Max(count, NestedSetComplexEntryAccess.Entries(Owner, _property, false).Count);
            count = Math.Max(count, NestedSetComplexEntryAccess.Entries(Owner, _property, true).Count);

            return count;
        }

        /// <summary>Reserves existing entry ordinals before descendant lifecycle transitions read them.</summary>
        private int PrepareElementSlots(
            ElementSnapshot[] addressed,
            int count,
            bool retiring
        )
        {
            // WHY: Existing current ordinals determine the required access extent before any slot is borrowed.
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var element in addressed)
            {
                count = Math.Max(count, NestedSetComplexEntryAccess.Ordinal(element.Native, false) + 1);
            }

            if (_property.ComplexType.GetFlattenedComplexProperties().Any(nested => nested.IsCollection))
            {
                // WHY: A Deleted owner's minus-one sentinel borrows a free access slot for its descendants.
                foreach (var element in addressed)
                {
                    var ordinal = NestedSetComplexEntryAccess.Ordinal(element.Native, false);

                    if (ordinal < 0)
                    {
                        element.Borrow(count++);
                    }
                    else if (!retiring)
                    {
                        element.KeepSlotAcrossDeletion(ordinal);
                    }
                }
            }

            return count;
        }

        /// <summary>Builds typed temporary storage from retained current or original element instances.</summary>
        private IList CreateStagingCollection(
            ElementSnapshot[] addressed,
            int count
        )
        {
            var items = new object?[count];

            foreach (var element in addressed)
            {
                var ordinal = NestedSetComplexEntryAccess.Ordinal(element.Native, false);

                if (ordinal >= 0 && items[ordinal] is null)
                {
                    items[ordinal] = Instance(element);
                }
            }

            var staged = NestedSetComplexEntryAccess.CreateCollection(_property, count);

            // WHY: Only slots without a retained CLR element need a filler for mapped collections rejecting null.
            var acceptsNull = staged.GetType() is { IsGenericType: true } type
                && type.GetGenericTypeDefinition() == typeof(List<>);

            var filler = acceptsNull ? null : FirstStagingItem(items);

            // WHY: Indexed access pairs each temporary slot without a Range/Select delegate or intermediate sequence.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < count; index++)
            {
                var item = StagingItemAt(items, index) ?? filler;

                if (item is null && !acceptsNull)
                {
                    break;
                }

                staged.Add(item);
            }

            return staged;
        }

        /// <summary>Finds the first retained nonnull fallback without constructing a projected iterator.</summary>
        private object? FirstStagingItem(
            object?[] items
        )
        {
            // WHY: A filler must use the same current/original precedence as every later staged slot.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < items.Length; index++)
            {
                if (StagingItemAt(items, index) is { } item)
                {
                    return item;
                }
            }

            return null;
        }

        /// <summary>Reads a temporary slot without inventing an entity or replacing retained references.</summary>
        private object? StagingItemAt(
            object?[] items,
            int index
        ) => items[index]
            ?? CurrentItem(index)
            ?? (_original is IList original && index < original.Count ? original[index] : null);
    }
}

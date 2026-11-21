namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal sealed partial class NestedSetCollectionBaseline
{
    /// <summary>Preserves one actual external owner graph, not an entry receipt for each imported row.</summary>
    private sealed record CollectionState(
        object Owner,
        object? Collection,
        object[] Members,
        object[] Snapshot,
        bool HasSnapshot
    )
    {
        /// <summary>Restores the caller's collection object and ordered references after input detachment.</summary>
        internal void RestoreCurrent(
            IUpdateEntry entry,
            INavigation navigation
        )
        {
            using var pause = NestedSetNotificationPause.Begin(entry, navigation, Collection);

            if (!ReferenceEquals(entry.GetCurrentValue(navigation), Collection))
            {
                NestedSetInsertionContract
                    .Instance
                    .Setter(navigation)
                    .SetClrValueUsingContainingEntity(Owner, Collection);
            }

            if (Collection is null)
            {
                return;
            }

            var current = ((IEnumerable)Collection)
                .Cast<object>()
                .ToArray();

            if (current.SequenceEqual(Members, ReferenceEqualityComparer.Instance))
            {
                return;
            }

            if (Collection is IList { IsReadOnly: false, IsFixedSize: false } list)
            {
                // WHY: Added-entry cleanup removes active inputs from a principal's CLR collection. Rebuild
                // the original order without Clear/Reset, including preprepared later-wave references.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var index = list.Count - 1; index >= 0; index--)
                {
                    list.RemoveAt(index);
                }

                foreach (var member in Members)
                {
                    list.Add(member);
                }
            }
            else
            {
                var accessor = navigation.GetCollectionAccessor()!;

                foreach (var member in current)
                {
                    accessor.RemoveStandalone(Collection, member);
                }

                foreach (var member in Members)
                {
                    accessor.AddStandalone(Collection, member);
                }
            }
        }
    }
}

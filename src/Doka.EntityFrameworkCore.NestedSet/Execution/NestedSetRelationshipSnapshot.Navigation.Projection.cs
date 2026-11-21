namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal sealed partial class NestedSetRelationshipSnapshot
{
    private sealed partial class NavigationState
    {
        /// <summary>Links repeated unselected anchors to their previous occurrence in linear scratch space.</summary>
        private static int[] IndexOccurrences(
            object[] current,
            out Dictionary<object, int> positions
        )
        {
            var indexed = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
            var previous = new int[current.Length];

            // WHY: Preserve every repeated reference occurrence without scanning the full list for each one.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < current.Length; index++)
            {
                previous[index] = indexed.GetValueOrDefault(current[index], -1);
                indexed[current[index]] = index;
            }

            positions = indexed;

            return previous;
        }

        /// <summary>Finds the gap after the last original anchor that still exists in pending CLR membership.</summary>
        private int TrailingOriginalGap(
            HashSet<object> affected,
            Dictionary<object, int> positions
        )
        {
            var gap = 0;

            foreach (var member in _members)
            {
                if (!affected.Contains(member) && positions.TryGetValue(member, out var position))
                {
                    gap = position + 1;
                }
            }

            return gap;
        }

        /// <summary>Places selected original occurrences while retaining both member subsequences.</summary>
        private Dictionary<int, List<object>> ProjectSelectedOccurrences(
            HashSet<object> affected,
            Dictionary<object, int> positions,
            int[] previous,
            int currentLength,
            int gap,
            out int count
        )
        {
            var insertions = new Dictionary<int, List<object>>();
            count = currentLength;
            var selectedGap = currentLength;

            // WHY: Reverse anchor chains retain duplicates and place selected entries before late raw additions.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = _members.Length - 1; index >= 0; index--)
            {
                var member = _members[index];

                if (!affected.Contains(member))
                {
                    if (positions.TryGetValue(member, out var position) && position >= 0)
                    {
                        gap = position;
                        positions[member] = previous[position];
                    }

                    continue;
                }

                // WHY: Conflicting raw anchor order must not reverse either retained subsequence.
                gap = Math.Min(gap, selectedGap);
                selectedGap = gap;

                if (!insertions.TryGetValue(gap, out var members))
                {
                    members = [];
                    insertions.Add(gap, members);
                }

                members.Add(member);
                count++;
            }

            return insertions;
        }

        /// <summary>Emits each pending unselected member and selected original occurrence exactly once.</summary>
        private static object[] MergeProjectedMembers(
            object[] current,
            Dictionary<int, List<object>> insertions,
            int count
        )
        {
            var result = new object[count];
            var offset = 0;

            // WHY: Gap ordinals interleave retained buckets with pending CLR members in one ordered pass.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index <= current.Length; index++)
            {
                if (insertions.TryGetValue(index, out var members))
                {
                    // WHY: Buckets were recorded in reverse original order while following duplicate anchors.
                    // ReSharper disable once ForCanBeConvertedToForeach
                    // ReSharper disable once LoopCanBeConvertedToQuery
                    for (var member = members.Count - 1; member >= 0; member--)
                    {
                        result[offset++] = members[member];
                    }
                }

                if (index < current.Length)
                {
                    result[offset++] = current[index];
                }
            }

            return result;
        }
    }
}

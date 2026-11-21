namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal sealed partial class NestedSetRelationshipSnapshot
{
    private sealed partial class NavigationState
    {
        /// <summary>Restores the original collection object and ordered contents, or the exact reference.</summary>
        internal void RestoreCurrent()
        {
            if (!_restore)
            {
                return;
            }

            // WHY: An unchanged owner's EF-moved member needs its original position, while unrelated raw
            // callback additions/removals remain pending. Existing changed-FK snapshots retain full authority.
            var expected = IsAuthoritative || !_navigation.IsCollection
                ? _members
                : ProjectMembers(_affected!);

            _restoredMembers = expected;

            // WHY: Supplemental recovery owns selected members, not unrelated raw additions. A formerly
            // null collection must retain the caller's new container when those pending members need it.
            _restoredCurrent = !IsAuthoritative && _navigation.IsCollection && _current is null && expected.Length > 0
                ? _entry.GetCurrentValue(_navigation)
                : _current;

            using var pause = NestedSetNotificationPause.Begin(
                _entry,
                _navigation,
                _navigation.IsCollection
                    ? _restoredCurrent
                    : null);

            if (!ReferenceEquals(_entry.GetCurrentValue(_navigation), _restoredCurrent))
            {
                NestedSetInsertionContract
                    .Instance
                    .Setter(_navigation)
                    .SetClrValueUsingContainingEntity(_entity, _restoredCurrent);
            }

            if (!_navigation.IsCollection
                || _restoredCurrent is null
                || Members(_restoredCurrent)
                    .SequenceEqual(expected, ReferenceEqualityComparer.Instance))
            {
                return;
            }

            if (_restoredCurrent is IList { IsReadOnly: false, IsFixedSize: false } list)
            {
                // WHY: Notification collections reject Reset/Clear. Individual removals preserve EF's event contract.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var index = list.Count - 1; index >= 0; index--)
                {
                    list.RemoveAt(index);
                }

                foreach (var member in expected)
                {
                    list.Add(member);
                }

                return;
            }

            var accessor = _navigation.GetCollectionAccessor()!;

            foreach (var member in Members(_restoredCurrent))
            {
                accessor.RemoveStandalone(_restoredCurrent, member);
            }

            foreach (var member in expected)
            {
                accessor.AddStandalone(_restoredCurrent, member);
            }
        }

        /// <summary>Extends native-only recovery without granting authority over the pending CLR sequence.</summary>
        internal void IncludeSnapshotChanges(
            HashSet<object> captured
        )
        {
            var current = _entry.GetRelationshipSnapshotValue(_navigation) as ISet<object>;
            var original = _snapshotMembers.ToHashSet(ReferenceEqualityComparer.Instance);
            HashSet<object>? selected = null;

            // WHY: Compare each deduplicated endpoint once; scanning a wide original array per dependent
            // would make native-only recovery quadratic. Newly introduced, uncaptured members remain pending.
            foreach (var member in _snapshotMembers)
            {
                if (current?.Contains(member) != true)
                {
                    SelectSnapshotMember(member, captured, ref selected);
                }
            }

            if (current is not null)
            {
                foreach (var member in current)
                {
                    if (!original.Contains(member))
                    {
                        SelectSnapshotMember(member, captured, ref selected);
                    }
                }
            }

            if (selected is not null)
            {
                _affected = selected;
                _restoreNative = true;
            }
        }

        /// <summary>Restores membership through EF's collection API instead of its scalar snapshot setter.</summary>
        internal void RestoreSnapshot()
        {
            if ((!_restore && !_restoreNative)
                || !_hasSnapshot)
            {
                return;
            }

            var contract = NestedSetInsertionContract.Instance;

            if (!_navigation.IsCollection)
            {
                contract.SetRelationshipSnapshot(_entry, _navigation, _snapshot);

                return;
            }

            foreach (var member in Members(_entry.GetRelationshipSnapshotValue(_navigation)))
            {
                if (IsAuthoritative || _affected!.Contains(member))
                {
                    contract.RemoveCollectionSnapshot(_entry, _navigation, member);
                }
            }

            foreach (var member in _snapshotMembers)
            {
                if (IsAuthoritative || _affected!.Contains(member))
                {
                    contract.AddCollectionSnapshot(_entry, _navigation, member);
                }
            }
        }

        /// <summary>Checks exact CLR references/order and installed snapshot membership.</summary>
        internal void Verify()
        {
            if (!_restore
                && !_restoreNative)
            {
                return;
            }

            var valid = !_restore || ReferenceEquals(_entry.GetCurrentValue(_navigation), _restoredCurrent);

            if (_navigation.IsCollection)
            {
                if (_restore)
                {
                    var members = Members(_restoredCurrent);
                    var expected = _restoredMembers ?? _members;
                    valid &= _unordered
                        ? members
                            .ToHashSet(ReferenceEqualityComparer.Instance)
                            .SetEquals(expected)
                        : members.SequenceEqual(expected, ReferenceEqualityComparer.Instance);
                }

                if (_hasSnapshot)
                {
                    var snapshot = Members(_entry.GetRelationshipSnapshotValue(_navigation))
                        .ToHashSet(ReferenceEqualityComparer.Instance);

                    valid &= IsAuthoritative
                        ? snapshot.SetEquals(_snapshotMembers)
                        : snapshot
                            .Where(_affected!.Contains)
                            .ToHashSet(ReferenceEqualityComparer.Instance)
                            .SetEquals(_snapshotMembers.Where(_affected.Contains));
                }
            }
            else if (_hasSnapshot)
            {
                valid &= ReferenceEquals(_entry.GetRelationshipSnapshotValue(_navigation), _snapshot);
            }

            if (!valid)
            {
                throw new InvalidOperationException("The coordinated save could not restore a navigation graph.");
            }
        }

        /// <summary>Restores selected original occurrences without reordering unrelated pending CLR members.</summary>
        private object[] ProjectMembers(
            HashSet<object> affected
        )
        {
            var current = Members(_entry.GetCurrentValue(_navigation))
                .Where(member => !affected.Contains(member))
                .ToArray();

            var previous = IndexOccurrences(current, out var positions);
            var gap = TrailingOriginalGap(affected, positions);
            var insertions = ProjectSelectedOccurrences(
                affected,
                positions,
                previous,
                current.Length,
                gap,
                out var count);

            return MergeProjectedMembers(current, insertions, count);
        }
    }
}

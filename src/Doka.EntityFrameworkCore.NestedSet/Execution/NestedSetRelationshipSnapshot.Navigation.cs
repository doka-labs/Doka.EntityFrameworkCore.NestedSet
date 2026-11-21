namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal sealed partial class NestedSetRelationshipSnapshot
{
    /// <summary>Retains current graph storage independently of EF's installed navigation snapshot.</summary>
    private sealed partial class NavigationState
    {
        private readonly object? _current;
        private readonly object _entity;
        private readonly IUpdateEntry _entry;
        private readonly bool _hasSnapshot;
        private readonly object[] _members;
        private readonly INavigation _navigation;
        private readonly object? _snapshot;
        private readonly object[] _snapshotMembers;
        private readonly bool _unordered;
        private HashSet<object>? _affected;
        private bool _restore;
        private bool _restoreNative;
        private object? _restoredCurrent;
        private object[]? _restoredMembers;

        /// <summary>Copies mutable membership without cloning any caller-owned entity.</summary>
        internal NavigationState(
            IUpdateEntry entry,
            INavigation navigation,
            bool authoritative
        )
        {
            _entry = entry;
            _navigation = navigation;
            _entity = entry.ToEntityEntry()
                .Entity;

            _current = entry.GetCurrentValue(navigation);
            _hasSnapshot = !navigation.IsCollection
                || entry.EntityType.GetChangeTrackingStrategy() == ChangeTrackingStrategy.Snapshot;

            _snapshot = _hasSnapshot
                ? entry.GetRelationshipSnapshotValue(navigation)
                : null;

            _members = navigation.IsCollection
                ? Members(_current)
                : [];

            _snapshotMembers = navigation.IsCollection
                ? Members(_snapshot)
                : [];

            // WHY: Reference navigation values are entities; only collection storage can have set ordering semantics.
            _unordered = navigation.IsCollection
                && _current
                    ?.GetType()
                    .GetInterfaces()
                    .Any(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ISet<>))
                == true;

            IsAuthoritative = authoritative;
        }

        /// <summary>Gets whether an existing pending relationship owns the complete captured navigation.</summary>
        internal bool IsAuthoritative { get; private set; }

        /// <summary>Gets the exact metadata identity shared with the captured dependent index.</summary>
        internal IForeignKey ForeignKey => _navigation.ForeignKey;

        /// <summary>Gets whether a supplemental collection's native membership differs from its preimage.</summary>
        internal bool HasNativeCollectionChange
        {
            get
            {
                if (IsAuthoritative
                    || !_navigation.IsCollection
                    || !_hasSnapshot)
                {
                    return false;
                }

                if (_entry.GetRelationshipSnapshotValue(_navigation) is not ISet<object> current)
                {
                    return _snapshotMembers.Length != 0;
                }

                if (current.Count != _snapshotMembers.Length)
                {
                    return true;
                }

                // ReSharper disable once LoopCanBeConvertedToQuery
                foreach (var member in _snapshotMembers)
                {
                    if (!current.Contains(member))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>Gets whether EF changed this reference independently of its scalar FK identity.</summary>
        internal bool HasNativeReferenceChange =>
            !_navigation.IsCollection && !ReferenceEquals(_entry.GetRelationshipSnapshotValue(_navigation), _snapshot);

        /// <summary>Keeps existing pending-relationship authority independent of endpoint capture order.</summary>
        internal void Promote(
            bool authoritative
        ) => IsAuthoritative |= authoritative;

        /// <summary>
        ///     Limits supplemental recovery to the exact dependents with native changes or identity writes.
        /// </summary>
        internal void PrepareRestore(
            Dictionary<IForeignKey, HashSet<object>>? changed
        )
        {
            _affected = null;
            _restoredMembers = null;
            _restoredCurrent = _current;
            _restore = IsAuthoritative;
            _restoreNative = false;

            if (IsAuthoritative
                || changed is null
                || !changed.TryGetValue(_navigation.ForeignKey, out var affected))
            {
                return;
            }

            _affected = affected;
            _restore = _navigation.IsOnDependent
                ? affected.Contains(_entity)
                : _navigation.IsCollection
                    ? _members.Any(affected.Contains)
                    || _snapshotMembers.Any(affected.Contains)
                    || ContainsAffected(_entry.GetCurrentValue(_navigation), true, affected)
                    : ContainsAffected(_current, false, affected)
                    || ContainsAffected(_snapshot, false, affected)
                    || ContainsAffected(_entry.GetCurrentValue(_navigation), false, affected);
        }

        /// <summary>Shares native selection only after a captured member has an uncovered membership change.</summary>
        private void SelectSnapshotMember(
            object member,
            HashSet<object> captured,
            ref HashSet<object>? selected
        )
        {
            if (!captured.Contains(member)
                || (_restore && _affected?.Contains(member) == true))
            {
                return;
            }

            selected ??= _affected is null
                ? new HashSet<object>(ReferenceEqualityComparer.Instance)
                : new HashSet<object>(_affected, ReferenceEqualityComparer.Instance);

            selected.Add(member);
        }

        /// <summary>Copies references in their current enumeration order, including caller-owned duplicates.</summary>
        private static object[] Members(
            object? collection
        ) => collection is IEnumerable values
            ? values
                .Cast<object>()
                .ToArray()
            : [];
    }
}

namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal sealed partial class NestedSetCollectionBaseline
{
    /// <summary>Groups only one active wave's persisted references by their exact external owner/navigation.</summary>
    internal sealed class SavedRelationships
    {
        private readonly Dictionary<(IUpdateEntry Entry, INavigation Navigation), HashSet<object>> _groups = new();
        private readonly IReadOnlySet<IUpdateEntry> _introduced;
        private readonly IReadOnlySet<object> _introducedEntities;
        private IUpdateAdapter? _updates;

        /// <summary>Retains the exact validated wave identities for external owner selection.</summary>
        /// <param name="introduced">The exact native insertion set already validated by the save guard.</param>
        /// <param name="introducedEntities">The save plan's corresponding reference-identity set.</param>
        internal SavedRelationships(
            IReadOnlySet<IUpdateEntry> introduced,
            IReadOnlySet<object> introducedEntities
        )
        {
            _introduced = introduced;
            _introducedEntities = introducedEntities;
        }

        /// <summary>Resolves an external tracked owner from the immutable persisted FK vector.</summary>
        internal void Add(
            IUpdateEntry entry,
            object entity,
            IForeignKey foreignKey,
            object?[] values
        )
        {
            if (foreignKey.IsOwnership
                || foreignKey.PrincipalToDependent is not { IsCollection: true } navigation
                || values.Any(value => value is null))
            {
                return;
            }

            _updates ??= entry
                .ToEntityEntry()
                .Context
                .GetService<IUpdateAdapterFactory>()
                .Create();

            var principal = _updates.TryGetEntry(foreignKey.PrincipalKey, values);

            if (principal is null
                || _introduced.Contains(principal)
                || principal.EntityType.GetChangeTrackingStrategy() != ChangeTrackingStrategy.Snapshot)
            {
                return;
            }

            if (!_groups.TryGetValue((principal, navigation), out var members))
            {
                members = new HashSet<object>(ReferenceEqualityComparer.Instance);
                _groups.Add((principal, navigation), members);
            }

            members.Add(entity);
        }

        /// <summary>Baselines exact persisted references with one collection scan per owner in this wave.</summary>
        internal void Record(
            ref NestedSetCollectionBaseline? baseline
        )
        {
            if (_groups.Count == 0)
            {
                return;
            }

            var journal = baseline ??= new NestedSetCollectionBaseline();

            foreach (var ((principal, navigation), saved) in _groups)
            {
                // WHY: A newly selected owner needs its rollback baseline even when EF already installed the
                // saved member. Capture before testing native membership, excluding only this active wave.
                journal.Capture(principal, navigation, _introducedEntities);

                if (principal.GetCurrentValue(navigation) is not IEnumerable collection)
                {
                    continue;
                }

                var snapshot = principal.GetRelationshipSnapshotValue(navigation) as ISet<object>;

                // WHY: Prepared lists can hold the whole import. A bounded reference set scans each owner once
                // per wave instead of invoking List.Contains separately for every newly saved member.
                foreach (var member in collection)
                {
                    if (saved.Remove(member)
                        && snapshot?.Contains(member) != true)
                    {
                        NestedSetInsertionContract.Instance.AddCollectionSnapshot(principal, navigation, member);
                    }

                    if (saved.Count == 0)
                    {
                        break;
                    }
                }
            }
        }
    }
}

namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal sealed partial class NestedSetRelationshipSnapshot
{
    /// <summary>Indexes only editable Parent domains whose authorized dependent can appear at a wrong target.</summary>
    private void CaptureParentDomain(
        IUpdateEntry entry,
        IForeignKey key
    )
    {
        var model = NestedSetModelMapping.For(entry.EntityType.Model);

        if (key.PrincipalToDependent is not { } navigation
            || !model.TryDescriptor(entry.EntityType, out var descriptor))
        {
            return;
        }

        var parent = descriptor.Parent.Resolve(entry.EntityType);

        if (!key.Properties.Contains(parent)
            || !NestedSetTrackedProperty.HasChanged(entry, parent))
        {
            return;
        }

        if (!_parentDependents.TryGetValue(key, out var dependents))
        {
            dependents = new HashSet<object>(ReferenceEqualityComparer.Instance);
            _parentDependents.Add(key, dependents);

            foreach (var principal in key.PrincipalEntityType.GetDerivedTypesInclusive())
            {
                if (!_parentDomains.TryGetValue(principal, out var domains))
                {
                    domains = [];
                    _parentDomains.Add(principal, domains);
                }

                domains.Add(new ParentDomain(navigation, dependents));
            }
        }

        dependents.Add(
            entry.ToEntityEntry()
                .Entity);
    }

    /// <summary>Includes a tracked principal referenced by the caller's pending CLR graph.</summary>
    private void CaptureRelatedPrincipal(
        IUpdateEntry entry,
        object? entity,
        INavigation reference,
        bool authoritative
    )
    {
        if (entity is null)
        {
            return;
        }

        var contract = NestedSetInsertionContract.Instance;
        var principal = contract.FindTrackedEntry(contract.Manager(entry), entity, reference.TargetEntityType);
        CapturePrincipal(principal, reference.Inverse, authoritative);
    }

    /// <summary>Shares each affected principal navigation snapshot across all changed dependents.</summary>
    private void CapturePrincipal(
        IUpdateEntry? entry,
        INavigation? navigation,
        bool authoritative
    )
    {
        if (entry is { EntityState: not EntityState.Detached }
            && navigation is not null)
        {
            CaptureNavigation(entry, navigation, authoritative);
        }
    }

    /// <summary>Copies a navigation once, retaining the original collection instance and member order.</summary>
    private void CaptureNavigation(
        IUpdateEntry entry,
        INavigation navigation,
        bool authoritative = true
    )
    {
        if (_navigations.TryGetValue((entry, navigation), out var existing))
        {
            existing.Promote(authoritative);

            return;
        }

        _navigations.Add((entry, navigation), new NavigationState(entry, navigation, authoritative));
    }

    /// <summary>Retains an additional native bucket only for an already divergent pending FK vector.</summary>
    private void CapturePendingBucket(
        IUpdateEntry entry,
        IForeignKey key,
        object?[] installed
    )
    {
        object?[]? current = null;

        // WHY: The common unchanged vector needs no second array or journal. A lazy EF cache can index
        // pending raw current values instead of installed snapshot values, so divergent vectors need both.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < installed.Length; index++)
        {
            var property = key.Properties[index];

            // WHY: The public update interface reports null for a conceptual null, hiding the underlying
            // pending raw key. PropertyEntry exposes that independently captured current/sidecar value.
            var value = entry.IsConceptualNull(property)
                ? entry
                    .ToEntityEntry()
                    .Property(property.Name)
                    .CurrentValue
                : entry.GetCurrentValue(property);

            if (!key
                .PrincipalKey
                .Properties[index]
                .GetKeyValueComparer()
                .Equals(value, installed[index]))
            {
                current ??= (object?[])installed.Clone();
                current[index] = NestedSetStructuralValue.Snapshot(property, value);
            }
        }

        if (current is not null
            && current.All(value => value is not null))
        {
            (_pendingBuckets ??= []).Add(
                new ForeignKeyState(entry, key, current, HasDependentMembership(entry, key, current)));
        }
    }

    /// <summary>Reads EF's exact indexed bucket without interpreting the entry's current/null flags.</summary>
    private static IEnumerable<IUpdateEntry>? DependentBucket(
        IUpdateEntry entry,
        IForeignKey key,
        object?[] values
    )
    {
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var value in values)
        {
            if (value is null)
            {
                return null;
            }
        }

        var contract = NestedSetInsertionContract.Instance;

        return contract.GetOrCreateDependents(contract.Manager(entry), values, key);
    }

    /// <summary>Reads actual bucket membership without inferring it from scalar or conceptual-null state.</summary>
    private static bool HasDependentMembership(
        IUpdateEntry entry,
        IForeignKey key,
        object?[] values
    ) =>

        // WHY: EF returns its indexed HashSet (or an empty sequence). Enumerable.Contains dispatches to
        // ICollection.Contains, so capturing one dependent does not enumerate its wide owner's bucket.
        DependentBucket(entry, key, values) is { } bucket && bucket.Contains(entry);
}

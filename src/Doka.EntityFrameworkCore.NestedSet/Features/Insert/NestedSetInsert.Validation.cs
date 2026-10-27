namespace Doka.EntityFrameworkCore.NestedSet.Features.Insert;

internal sealed partial class NestedSetInsert<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    /// <summary>Rejects callbacks that alter the exact staged structure or add another hierarchy entity.</summary>
    /// <param name="entry">The introduced root whose current structural values remain staged.</param>
    /// <param name="destination">The placement resolved under the exact tree lock.</param>
    /// <param name="identity">The installed temporary or provider-generated identity captured before callbacks.</param>
    /// <param name="hasAssignedKey">Whether the caller supplied an identity before insertion.</param>
    /// <param name="key">The independent original assigned-key snapshot.</param>
    /// <param name="scope">The exact expected scope snapshot.</param>
    /// <param name="treeId">The exact expected tree identity snapshot.</param>
    /// <param name="parent">The exact expected parent snapshot.</param>
    /// <param name="knownTracked">The caller's earlier tracked hierarchy instances.</param>
    private void RequireSavedStage(
        EntityEntry<TEntity> entry,
        NestedSetPlacementResolver<TEntity, TKey, TTreeId, TScope>.Destination destination,
        NestedSetInsertionTracking.Identity identity,
        bool hasAssignedKey,
        TKey key,
        TScope scope,
        TTreeId treeId,
        NestedSetParent<TKey> parent,
        HashSet<object> knownTracked
    )
    {
        var values = entry.CurrentValues;

        // WHY: Managed saves bypass ordinary direct-write guards. Validate before ordering or refresh can hide
        // a callback's structural edit, using representation equality rather than a broader application comparer.
        if (entry.State == EntityState.Detached
            || HasUnexpectedHierarchyEntry(entry, knownTracked)
            || !identity.Matches(_store.Map.KeyProperty)
            || (hasAssignedKey
                && !NestedSetTypedValue<TKey>.Matches(
                    _store.Map.KeyProperty,
                    NestedSetTypedValue<TKey>.Read(values, _store.Map.KeyProperty),
                    key))
            || (_store.Map.ScopeProperty is { } scopeProperty
                && !NestedSetTypedValue<TScope>.Matches(
                    scopeProperty,
                    NestedSetTypedValue<TScope>.Read(values, scopeProperty),
                    scope))
            || !NestedSetTypedValue<TTreeId>.Matches(
                _store.Map.TreeIdProperty,
                NestedSetTypedValue<TTreeId>.Read(values, _store.Map.TreeIdProperty),
                treeId)
            || !NestedSetParent<TKey>
                .Read(values, _store.Map.ParentProperty)
                .Matches(_store.Map.ParentProperty, parent)
            || NestedSetTypedValue<long>.Read(values, _store.Map.LeftProperty) != destination.Boundary
            || NestedSetTypedValue<long>.Read(values, _store.Map.RightProperty) != checked(destination.Boundary + 1)
            || NestedSetTypedValue<int>.Read(values, _store.Map.DepthProperty) != destination.Depth
            || NestedSetTypedValue<long>.Read(values, _store.Map.PositionProperty) != destination.Position)
        {
            throw new NestedSetException(
                NestedSetErrorCode.InvalidStructure,
                "Save callbacks must not alter managed hierarchy values or tracked nodes during an insertion.");
        }
    }

    /// <summary>Finds hierarchy entries introduced by callbacks without repeating global change detection.</summary>
    private bool HasUnexpectedHierarchyEntry(
        EntityEntry root,
        HashSet<object> knownTracked
    )
    {
        var tracker = _store.Context.ChangeTracker;
        var automaticDetection = tracker.AutoDetectChangesEnabled;

        try
        {
            // WHY: The save guard already detects callback changes before SQL. After the save, only new tracked
            // identities matter here; property values on the root are checked separately below.
            tracker.AutoDetectChangesEnabled = false;

            return NestedSetEntityAccess<TEntity>
                .Entries(_store.Context, _store.Map.EntityType)
                .Any(candidate => !ReferenceEquals(candidate.Entity, root.Entity)
                    && !knownTracked.Contains(candidate.Entity));
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = automaticDetection;
        }
    }
}

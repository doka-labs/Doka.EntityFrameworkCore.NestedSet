namespace Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

internal static partial class NestedSetSaveChanges
{
    private static readonly ConditionalWeakTable<IEntityType, NavigationProbe[]> s_hierarchyNavigations = new();

    /// <summary>Observes hierarchy work without changing EF's write set or retaining full input collections.</summary>
    /// <param name="context">The context whose exact configured hierarchy entries are checked.</param>
    /// <returns>Whether a tracked hierarchy has pending structural, ordering, or relevant navigation changes.</returns>
    internal static bool HasHierarchyChanges(
        DbContext context
    )
        => HasHierarchyChanges(context, null);

    /// <summary>Observes every hierarchy while allowing only the exact authorized scalar plan.</summary>
    /// <param name="context">The context checked without modifying EF state.</param>
    /// <param name="planned">Existing candidates captured before Parent suppression, or no exceptions.</param>
    /// <param name="insertions">Native entries whose staged insertion scalar contract is validated separately.</param>
    /// <param name="allowMarkedTriggers">Whether marked Parent/order edits may enter initial planning.</param>
    /// <returns>Whether any hierarchy scalar, lifecycle, or relevant navigation exceeds the authorized plan.</returns>
    private static bool HasHierarchyChanges(
        DbContext context,
        Dictionary<IUpdateEntry, CandidatePlan>? planned,
        HashSet<IUpdateEntry>? insertions = null,
        bool allowMarkedTriggers = false
    )
    {
        var model = NestedSetModelMapping.For(context.Model);
        var updates = context
            .GetService<IUpdateAdapterFactory>()
            .Create();

        var matchedCandidates = 0;
        var importInputs = s_contexts.TryGetValue(context, out var state) && state.ManagedDepth > 0
            ? state.ImportInputs
            : null;

        foreach (var entry in updates.Entries)
        {
            if (entry.EntityState == EntityState.Detached)
            {
                continue;
            }

            if (model.TryDescriptor(entry.EntityType, out var descriptor))
            {
                if (planned is not null
                    && planned.TryGetValue(entry, out var candidate))
                {
                    if (!candidate.Matches(entry))
                    {
                        return true;
                    }

                    matchedCandidates++;
                }
                else if (insertions?.Contains(entry) != true)
                {
                    if (entry.EntityState is EntityState.Added or EntityState.Deleted)
                    {
                        return true;
                    }

                    var structure = model.StructuralProperties(entry.EntityType);
                    var parent = allowMarkedTriggers ? descriptor.Parent.Resolve(entry.EntityType) : null;

                    // WHY: These read-only wrappers otherwise allocate an interface enumerator per tracked node.
                    // ReSharper disable once ForCanBeConvertedToForeach
                    // ReSharper disable once LoopCanBeConvertedToQuery
                    for (var index = 0; index < structure.Count; index++)
                    {
                        var property = structure[index];

                        if (UnplannedPropertyChanged(
                                entry,
                                property,
                                property == parent && entry.IsModified(property)))
                        {
                            return true;
                        }
                    }

                    if (model.Ordering(entry.EntityType) is { } ordering)
                    {
                        // WHY: Reuse cached metadata without an interface enumerator per node and pass.
                        // ReSharper disable once ForCanBeConvertedToForeach
                        // ReSharper disable once LoopCanBeConvertedToQuery
                        for (var index = 0; index < ordering.Properties.Count; index++)
                        {
                            var property = ordering.Properties[index];

                            if (UnplannedPropertyChanged(
                                    entry,
                                    property,
                                    allowMarkedTriggers && entry.IsModified(property)))
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            // WHY: A non-hierarchy principal can expose a collection whose FK supplies a hierarchy Scope.
            // Observe that relationship without detecting the principal's unrelated payload.
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var probe in s_hierarchyNavigations.GetValue(entry.EntityType, CreateHierarchyNavigations))
            {
                var navigation = probe.Navigation;

                // WHY: Notification collections have no relationship-snapshot slot. Their FK fixup/Modified
                // flags were already observed above; reading that nonexistent slot would index -1 in EF.
                if (navigation.IsCollection
                    && entry.EntityType.GetChangeTrackingStrategy() != ChangeTrackingStrategy.Snapshot)
                {
                    continue;
                }

                if (NavigationChangesHierarchy(entry, probe, model, updates, planned, importInputs))
                {
                    return true;
                }
            }
        }

        // WHY: Detached candidates disappear from adapter.Entries and cannot be mistaken for a valid empty save.
        return planned is not null && matchedCandidates != planned.Count;
    }

    /// <summary>Allows a marked trigger without allowing a conceptually null required relationship.</summary>
    private static bool UnplannedPropertyChanged(
        IUpdateEntry entry,
        IProperty property,
        bool authorized
    )
        => entry.IsConceptualNull(property) || (!authorized && NestedSetTrackedProperty.HasChanged(entry, property));

    /// <summary>Retains navigations whose FK fixup can change hierarchy roles, order, or dependent lifecycle.</summary>
    private static NavigationProbe[] CreateHierarchyNavigations(
        IEntityType entityType
    )
    {
        var model = NestedSetModelMapping.For(entityType.Model);
        var result = new List<NavigationProbe>();

        foreach (var navigation in entityType.GetNavigations())
        {
            var foreignKey = navigation.ForeignKey;

            if (navigation.IsOnDependent)
            {
                if (IsHierarchyForeignKey(entityType, foreignKey, model))
                {
                    result.Add(new NavigationProbe(navigation, null));
                }

                continue;
            }

            // WHY: An inherited FK can be declared on an unconfigured base while its concrete dependent owns
            // the hierarchy contract. Resolve the concrete mappings once without including ordinary siblings.
            var dependents = foreignKey
                .DeclaringEntityType
                .GetDerivedTypesInclusive()
                .Where(dependent => !dependent.ClrType.IsAbstract)
                .ToArray();

            var included = dependents
                .Where(dependent => IsHierarchyForeignKey(dependent, foreignKey, model))
                .ToHashSet();

            if (included.Count == 0)
            {
                continue;
            }

            Func<object, bool>? includesDependent = null;

            if (included.Count != dependents.Length)
            {
                includesDependent = related =>
                {
                    var target = navigation.TargetEntityType;

                    // WHY: Named/shared mappings are identified by the navigation, never by their shared CLR type.
                    // Ordinary runtime lookup also resolves EF proxy subclasses to their exact mapped entity type.
                    var actual = target.HasSharedClrType
                        ? target
                        : target.Model.FindRuntimeEntityType(related.GetType());

                    return actual is not null && included.Contains(actual);
                };
            }

            result.Add(new NavigationProbe(navigation, includesDependent));
        }

        return result.ToArray();
    }

    /// <summary>Checks role/order effects and required severance that can delete a hierarchy dependent.</summary>
    private static bool IsHierarchyForeignKey(
        IEntityType dependent,
        IForeignKey foreignKey,
        NestedSetModelMapping model
    ) => model.TryDescriptor(dependent, out _)
        && (foreignKey.Properties.Any(property => model
                    .StructuralProperties(dependent)
                    .Contains(property)
                || model
                    .Ordering(dependent)
                    ?.Properties
                    .Contains(property)
                == true)
            || (foreignKey.IsRequired
                && (foreignKey.DeleteBehavior is DeleteBehavior.Cascade or DeleteBehavior.ClientCascade
                    || s_nullingProperties
                        .GetValue(foreignKey, CreateNullingProperties)
                        .All(pair => pair.Dependent.IsKey()))));

    /// <summary>Caches navigation ownership; only mixed concrete mappings need a dependent filter.</summary>
    /// <param name="Navigation">The exact mapped navigation whose relationship snapshot is observed.</param>
    /// <param name="IncludesDependent">The mixed-mapping filter, or null when all dependents are relevant.</param>
    private readonly record struct NavigationProbe(
        INavigation Navigation,
        Func<object, bool>? IncludesDependent
    );
}

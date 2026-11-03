namespace Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

internal static partial class NestedSetSaveChanges
{
    private static readonly ConditionalWeakTable<IForeignKey, NullingProperty[]> s_nullingProperties = new();

    /// <summary>Observes the FK subset EF can sever and the resulting hierarchy lifecycle effect.</summary>
    private static bool NullingChangesHierarchy(
        IUpdateEntry dependent,
        IUpdateEntry? oldPrincipal,
        IForeignKey foreignKey,
        NestedSetModelMapping model,
        IUpdateAdapter updates,
        Dictionary<IUpdateEntry, CandidatePlan>? planned
    )
    {
        var plannedParent = planned is not null
            && planned.TryGetValue(dependent, out var candidate)
            && foreignKey.Properties.Any(property => candidate.TryParent(property, out _));

        if (!plannedParent)
        {
            var currentPrincipal = updates.FindPrincipal(dependent, foreignKey);

            if (currentPrincipal is not null
                && !ReferenceEquals(currentPrincipal, oldPrincipal))
            {
                // WHY: EF does not sever an obsolete relationship when the live FK resolves to another principal.
                return false;
            }
        }
        else if (oldPrincipal is null)
        {
            // WHY: Pair comparisons need a tracked old principal. With a detached old target, EF first checks
            // whether the intended FK already resolves elsewhere; the temporarily suppressed Parent cannot do that.
            var values = new object?[foreignKey.Properties.Count];

            // WHY: Preserve the principal key's exact FK ordinals; this allocation is limited to the absent-old case.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = ParentValue(dependent, foreignKey.Properties[index], planned);
            }

            if (values.All(value => value is not null)
                && updates.TryGetEntry(foreignKey.PrincipalKey, values) is { } currentPrincipal
                && foreignKey.PrincipalEntityType.IsAssignableFrom(currentPrincipal.EntityType))
            {
                return false;
            }
        }

        // WHY: For tracked old principals the selected pairs include this mapping's exact Parent self-FK.
        // Compare intended Parent directly; the suppressed physical FK would hide actual severance.
        var properties = s_nullingProperties.GetValue(foreignKey, CreateNullingProperties);
        var onlyKeys = true;

        if (oldPrincipal is not null)
        {
            foreach (var pair in properties)
            {
                if (!pair
                        .Principal
                        .GetKeyValueComparer()
                        .Equals(
                            ParentValue(dependent, pair.Dependent, planned),
                            oldPrincipal.GetCurrentValue(pair.Principal)))
                {
                    return false;
                }

                onlyKeys &= pair.Dependent.IsKey();
            }
        }

        // WHY: EF can delete an unchanged required dependent when only keys remain, even with a detached old
        // principal. Required non-key cascade severance can also delete it after conceptual-null processing.
        if (foreignKey.IsRequired
            && (onlyKeys || foreignKey.DeleteBehavior is DeleteBehavior.Cascade or DeleteBehavior.ClientCascade))
        {
            return true;
        }

        var structure = model.StructuralProperties(dependent.EntityType);
        var ordering = model.Ordering(dependent.EntityType);

        foreach (var pair in properties)
        {
            var property = pair.Dependent;

            if (!property.IsKey()
                && (structure.Contains(property) || ordering?.Properties.Contains(property) == true)
                && ParentValue(dependent, property, planned) is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Uses a planned Parent only for its exact dependent; every other FK member remains live.</summary>
    private static object? ParentValue(
        IUpdateEntry dependent,
        IProperty property,
        Dictionary<IUpdateEntry, CandidatePlan>? planned
    ) => planned is not null
        && planned.TryGetValue(dependent, out var candidate)
        && candidate.TryParent(property, out var value)
            ? value
            : dependent.GetCurrentValue(property);

    /// <summary>Matches EF's minimal-overlap nulling selection using immutable public model metadata.</summary>
    private static NullingProperty[] CreateNullingProperties(
        IForeignKey foreignKey
    )
    {
        var selected = new List<NullingProperty>();
        var properties = foreignKey.Properties;

        // WHY: Keep principal/dependent ordinals paired while excluding members shared with another FK, exactly
        // as EF fixup does. If every member overlaps, EF falls back to the complete original FK.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < properties.Count; index++)
        {
            var property = properties[index];

            if (properties.Count == 1
                || !property
                    .GetContainingForeignKeys()
                    .Skip(1)
                    .Any())
            {
                selected.Add(new NullingProperty(property, foreignKey.PrincipalKey.Properties[index]));
            }
        }

        if (selected.Count == 0)
        {
            // WHY: An empty selection would miss required severance and key-only dependent deletion.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < properties.Count; index++)
            {
                selected.Add(new NullingProperty(properties[index], foreignKey.PrincipalKey.Properties[index]));
            }
        }

        return selected.ToArray();
    }

    /// <summary>Keeps a selected dependent member paired with its corresponding principal-key comparer.</summary>
    /// <param name="Dependent">The FK member EF may null unless it belongs to a key.</param>
    /// <param name="Principal">The corresponding key member used to identify the obsolete relationship.</param>
    private readonly record struct NullingProperty(
        IProperty Dependent,
        IProperty Principal
    );
}

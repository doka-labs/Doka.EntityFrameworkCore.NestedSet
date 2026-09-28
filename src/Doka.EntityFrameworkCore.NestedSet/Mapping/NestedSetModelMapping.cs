namespace Doka.EntityFrameworkCore.NestedSet.Mapping;

/// <summary>Indexes hierarchy metadata once per EF model for repeated save candidate scans.</summary>
internal sealed class NestedSetModelMapping
{
    // WHY: The weak key follows model lifetime; cached descriptors retain neither a context nor provider services.
    private static readonly ConditionalWeakTable<IModel, NestedSetModelMapping> s_models = new();

    private readonly Dictionary<IEntityType, NestedSetModelDescriptor> _descriptors = new();
    private readonly Dictionary<IEntityType, NestedSetOrdering> _ordering = new();
    private readonly Dictionary<IEntityType, IReadOnlyList<IProperty>> _structure = new();

    /// <summary>Resolves guarded structure for every hierarchy and ordering for configured entities.</summary>
    private NestedSetModelMapping(
        IModel model
    )
    {
        foreach (var entity in model.GetEntityTypes())
        {
            if (!NestedSetModelValidator.IsNestedSet(entity))
            {
                continue;
            }

            var descriptor = NestedSetModelDescriptor.FromFinalized(entity);
            _descriptors.Add(entity, descriptor);
            var properties = descriptor
                .StructuralProperties
                .Select(property => property.Resolve(entity))
                .ToArray();

            _structure.Add(entity, Array.AsReadOnly(properties));

            if (descriptor.Order.Count == 0)
            {
                continue;
            }

            var order = NestedSetOrdering.Create(entity, descriptor)
                ?? throw new InvalidOperationException("The configured nested-set order is missing.");

            _ordering.Add(entity, order);
        }

        // WHY: EF update entries carry concrete TPH/TPT metadata while the hierarchy contract can
        // be configured on their mapped base. Resolve that relationship once per model, not per save entry.
        foreach (var entity in model.GetEntityTypes())
        {
            if (_descriptors.ContainsKey(entity))
            {
                continue;
            }

            for (var current = entity.BaseType; current is not null; current = current.BaseType)
            {
                if (!_descriptors.TryGetValue(current, out var descriptor))
                {
                    continue;
                }

                _descriptors.Add(entity, descriptor);
                _structure.Add(entity, _structure[current]);

                if (_ordering.TryGetValue(current, out var ordering))
                {
                    _ordering.Add(entity, ordering);
                }

                break;
            }
        }
    }

    /// <summary>Returns shared finalized metadata without retaining the requesting context.</summary>
    internal static NestedSetModelMapping For(
        IModel model
    ) => s_models.GetValue(model, static value => new NestedSetModelMapping(value));

    /// <summary>Gets whether any hierarchy in the model participates in configured ordering.</summary>
    internal bool HasOrdering => _ordering.Count != 0;

    /// <summary>Gets whether the model contains at least one configured hierarchy.</summary>
    internal bool HasHierarchies => _descriptors.Count != 0;

    /// <summary>Gets the single immutable role and ordering contract for one nested-set entity.</summary>
    internal NestedSetModelDescriptor Descriptor(
        IEntityType entity
    ) => _descriptors.TryGetValue(entity, out var descriptor)
        ? descriptor
        : throw new InvalidOperationException($"Entity '{entity.Name}' is not configured as a nested set.");

    /// <summary>Attempts to get the immutable role contract for one entity type.</summary>
    /// <param name="entity">The finalized entity metadata.</param>
    /// <param name="descriptor">The descriptor when the entity is a configured hierarchy.</param>
    /// <returns>Whether the entity is configured as a nested set.</returns>
    internal bool TryDescriptor(
        IEntityType entity,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out NestedSetModelDescriptor? descriptor
    ) => _descriptors.TryGetValue(entity, out descriptor);

    /// <summary>Gets the cached order, including its key tiebreaker, or null for an unordered entity.</summary>
    internal NestedSetOrdering? Ordering(
        IEntityType entity
    ) => _ordering.GetValueOrDefault(entity);

    /// <summary>Gets guarded scalar metadata for a configured hierarchy entity.</summary>
    internal IReadOnlyList<IProperty> StructuralProperties(
        IEntityType entity
    ) => _structure[entity];
}

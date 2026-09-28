namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Reconciles ordinary access paths without attaching ownership metadata to migrations or snapshots.</summary>
internal static class NestedSetIndexes
{
    /// <summary>Tracks only indexes created during the lifetime of a mutable entity model.</summary>
    private static readonly ConditionalWeakTable<IConventionEntityType, List<IConventionIndex>> s_owned = new();

    /// <summary>Ensures mapped structural queries have ordinary indexes and removes unused owned indexes.</summary>
    /// <param name="entity">The mutable entity metadata being configured.</param>
    /// <param name="validateModel">Whether the finalized model contract must be validated.</param>
    /// <exception cref="InvalidOperationException">A finalized structural role or identity is invalid.</exception>
    internal static void Reconcile(
        IConventionEntityType entity,
        bool validateModel = false
    )
    {
        var desired = DesiredIndexes(entity, validateModel);
        if (desired.Count == 0)
        {
            return;
        }

        var owned = s_owned.GetValue(entity, _ => []);

        // WHY: Explicit application configuration adopts an index even when it repeats the convention value.
        owned.RemoveAll(index => !index.IsInModel || !IsUnmodified(index));

        foreach (var index in owned)
        {
            // WHY: Table and column mappings may be configured after HasNestedSet; physical names follow the final map.
            index.SetDatabaseName(PhysicalName(index));
        }

        var retained = new HashSet<IConventionIndex>();

        foreach (var definition in desired)
        {
            var existing = entity
                .GetIndexes()
                .Where(index => index.Properties.SequenceEqual(definition.Properties)
                    && IsOrdinary(index, definition.Descending))
                .OrderBy(owned.Contains)
                .FirstOrDefault();

            if (existing is null)
            {
                existing = (entity.FindIndex(definition.Properties) is null
                        ? entity.AddIndex(definition.Properties)
                        : entity.AddIndex(
                            definition.Properties,
                            AvailableName(entity, definition.Properties, definition.Descending)))
                    ?? throw new InvalidOperationException("Another model convention rejected a nested-set index.");

                if (definition.Descending.Any(value => value))
                {
                    existing.SetIsDescending(definition.Descending);
                }

                existing.SetDatabaseName(PhysicalName(existing));

                owned.Add(existing);
            }

            retained.Add(existing);
        }

        foreach (var obsolete in owned
                     .Where(index => !retained.Contains(index))
                     .ToArray())
        {
            // WHY: Reference ownership plus unmodified convention metadata cannot delete a user-created index.
            entity.RemoveIndex(obsolete);
            owned.Remove(obsolete);
        }
    }

    /// <summary>Builds tree-local access paths from complete structural annotations.</summary>
    /// <param name="entity">The mutable entity metadata.</param>
    /// <param name="validateModel">Whether incomplete metadata must fail instead of postponing reconciliation.</param>
    /// <returns>The ordered index definitions required by the configured hierarchy.</returns>
    private static List<DesiredIndex> DesiredIndexes(
        IConventionEntityType entity,
        bool validateModel
    )
    {
        if (!NestedSetModelValidator.IsNestedSet(entity))
        {
            return [];
        }

        var mapping = NestedSetModelValidator.Validate(entity, validateModel);
        if (mapping is null)
        {
            return [];
        }

        var prefix = mapping.Scope is null
            ? new[] { mapping.TreeId.Resolve(entity) }
            : new[] { mapping.Scope.Value.Resolve(entity), mapping.TreeId.Resolve(entity) };

        var left = mapping.Left.Resolve(entity);
        var right = mapping.Right.Resolve(entity);
        var parent = mapping.Parent.Resolve(entity);
        var position = mapping.Position.Resolve(entity);

        var desired = new List<DesiredIndex>(mapping.Order.Count == 0 ? 3 : 4)
        {
            Ascending(prefix.Append(left)),
            Ascending(prefix.Append(right)),
            Ascending(
                prefix
                    .Append(parent)
                    .Append(position)),
        };

        if (mapping.Order.Count > 0)
        {
            var properties = prefix
                .Append(parent)
                .Concat(mapping.Order.Select(order => order.Property.Resolve(entity)))
                .Append(mapping.NodeKey.Resolve(entity))
                .ToArray();

            var descending = new bool[properties.Length];

            for (var index = 0; index < mapping.Order.Count; index++)
            {
                descending[prefix.Length + 1 + index] = mapping.Order[index].Descending;
            }

            // WHY: The final ascending node key turns non-unique domain criteria into a stable total order.
            desired.Add(new DesiredIndex(properties, descending));
        }

        return desired;
    }

    /// <summary>Creates an all-ascending definition from one materialized property sequence.</summary>
    /// <param name="properties">The properties in index-key order.</param>
    /// <returns>An immutable desired-index definition.</returns>
    private static DesiredIndex Ascending(
        IEnumerable<IConventionProperty> properties
    )
    {
        var materialized = properties.ToArray();

        return new DesiredIndex(materialized, new bool[materialized.Length]);
    }

    /// <summary>Recognizes complete B-tree paths with the exact requested direction sequence.</summary>
    /// <param name="index">The configured model index.</param>
    /// <param name="descending">The required direction for every key property.</param>
    /// <returns>Whether the index can serve the complete requested access path.</returns>
    private static bool IsOrdinary(
        IConventionIndex index,
        IReadOnlyList<bool> descending
    )
    {
        // WHY: Filters, prefix lengths, expressions, operator metadata, and specialized methods cannot guarantee
        // the full ordered access path. Unknown provider metadata is preserved but never assumed equivalent.

        return !index.IsUnique
            && DirectionsMatch(index.IsDescending, descending)
            && index
                .GetAnnotations()
                .All(IsCompatibleAnnotation);
    }

    /// <summary>Compares EF's null-for-all-ascending representation with an explicit direction vector.</summary>
    /// <param name="actual">The directions stored by EF, or null for an ascending index.</param>
    /// <param name="expected">The directions required by the access path.</param>
    /// <returns>Whether every property direction matches.</returns>
    private static bool DirectionsMatch(
        IReadOnlyList<bool>? actual,
        IReadOnlyList<bool> expected
    ) => actual is null
        ? expected.All(value => !value)
        : actual.Count == expected.Count && actual.SequenceEqual(expected);

    /// <summary>Recognizes documented annotations that preserve complete B-tree key lookups.</summary>
    /// <param name="annotation">One index annotation.</param>
    /// <returns>Whether the annotation preserves ordinary B-tree key semantics.</returns>
    private static bool IsCompatibleAnnotation(
            IConventionAnnotation annotation
        ) // WHY: Covering columns, page fill, and concurrent creation do not change B-tree key lookup semantics.
        // Recognizing their public API metadata avoids duplicate writes without a runtime provider dependency.
        => annotation.Name switch
        {
            RelationalAnnotationNames.Name => true,
            RelationalAnnotationNames.Filter => annotation.Value is null,
            "SqlServer:Include" => annotation.Value is string[],
            "SqlServer:Clustered" or "SqlServer:Online" => annotation.Value is bool,
            "SqlServer:FillFactor" => annotation.Value is int and >= 1 and <= 100,
            "SqlServer:SortInTempDb" => annotation.Value is bool,
            "Npgsql:IndexMethod" => annotation.Value is "btree",
            "Npgsql:IndexInclude" => annotation.Value is string[],
            "Npgsql:CreatedConcurrently" => annotation.Value is bool,
            "Npgsql:StorageParameter:fillfactor" => annotation.Value is int and >= 10 and <= 100,
            _ => false,
        };

    /// <summary>Allows cleanup only while the application has not adopted or customized an owned index.</summary>
    /// <param name="index">The convention-created index to inspect.</param>
    /// <returns>Whether the index remains owned exclusively by this convention.</returns>
    private static bool IsUnmodified(
        IConventionIndex index
    ) => index.GetConfigurationSource() == ConfigurationSource.Convention
        && index.GetIsUniqueConfigurationSource() is null
        && index.GetIsDescendingConfigurationSource() is null or ConfigurationSource.Convention
        && index
            .GetAnnotations()
            .All(annotation => annotation.Name == RelationalAnnotationNames.Name
                && annotation.GetConfigurationSource() == ConfigurationSource.Convention);

    /// <summary>Names an additional ordinary index when another index uses the same properties.</summary>
    /// <param name="entity">The declaring entity.</param>
    /// <param name="properties">The index-key properties.</param>
    /// <param name="descending">The requested directions.</param>
    /// <returns>An available deterministic model index name.</returns>
    private static string AvailableName(
        IConventionEntityType entity,
        IReadOnlyList<IConventionProperty> properties,
        IReadOnlyList<bool> descending
    )
    {
        // WHY: A short stable hash avoids provider identifier limits and schema-wide name collisions without using
        // provider APIs or persisting ownership annotations. Direction is part of an ordered index's identity.
        var identity = entity.Name
            + "\n"
            + string.Join("\n", properties.Select(property => property.Name))
            + "\n"
            + string.Concat(descending.Select(value => value ? 'D' : 'A'));

        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        var stem = string.Concat("IX_NestedSet_", digest.AsSpan(0, 24));
        var name = stem;
        var suffix = 0;

        while (entity.FindIndex(name) is not null)
        {
            suffix++;
            name = stem + "_" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return name;
    }

    /// <summary>Builds a convention-owned index's physical identity from the final table and column map.</summary>
    /// <param name="index">The named model index.</param>
    /// <returns>A deterministic provider-safe physical index name.</returns>
    private static string PhysicalName(
        IConventionIndex index
    )
    {
        var entity = index.DeclaringEntityType;
        var table = NestedSetStoreObject.TryResolve(entity, index.Properties)
            ?? StoreObjectIdentifier.Create(entity, StoreObjectType.Table);

        // WHY: HasNestedSet can run before later entity-splitting configuration. The finalizing convention invokes
        // this method again and replaces the provisional primary-table identity with the completed fragment map.
        var columns = index.Properties.Select(property => table is { } store
            ? property.GetColumnName(store)
            : property.Name);

        var directions = index.IsDescending is null
            ? string.Empty
            : string.Concat(index.IsDescending.Select(value => value ? 'D' : 'A'));

        // WHY: Separate contexts can map the same CLR type into different tables in one schema. NUL separators
        // distinguish identifier parts without ambiguities from legal punctuation in database names.
        var identity = string.Join("\0", new[] { index.Name, table?.Schema, table?.Name, directions }.Concat(columns));
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));

        return string.Concat("IX_NestedSet_", digest.AsSpan(0, 24));
    }

    /// <summary>Pairs one ordered property path with its exact descending-key flags.</summary>
    /// <param name="Properties">The properties in index-key order.</param>
    /// <param name="Descending">The direction matching each property.</param>
    private readonly record struct DesiredIndex(
        IConventionProperty[] Properties,
        bool[] Descending
    );
}

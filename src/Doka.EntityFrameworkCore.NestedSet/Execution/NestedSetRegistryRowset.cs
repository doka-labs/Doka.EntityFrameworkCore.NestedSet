using System.Data.Common;

namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Supplies provider-neutral mapped rowset values without owning native SQL syntax.</summary>
internal static class NestedSetRegistryRowset
{
    /// <summary>Bounds scalar rowsets before a native adapter switches to one collection parameter.</summary>
    internal const int ScalarRowLimit = 256;

    /// <summary>Counts scalar identity parameters or the single serialized collection parameter.</summary>
    /// <param name="requestCount">The number of tree identities in the completed lock plan.</param>
    /// <param name="scoped">Whether each identity also contains a scope value.</param>
    /// <returns>The exact parameter capacity for adapters using the shared scalar threshold.</returns>
    internal static int ParameterCount(
        int requestCount,
        bool scoped
    ) => requestCount >= ScalarRowLimit ? 1 : requestCount * (scoped ? 2 : 1);

    /// <summary>Appends mapped identity tuples without selecting an adapter's source or reservation syntax.</summary>
    /// <param name="rows">The native renderer's tuple buffer.</param>
    /// <param name="parameters">Receives parameters in the same order as their positional placeholders.</param>
    /// <param name="command">Creates parameters for the actual native connection.</param>
    /// <param name="mapping">The finalized registry's mapped identity columns.</param>
    /// <param name="requests">The completed lock requests in their original ordinal order.</param>
    internal static void AppendScalarRows(
        StringBuilder rows,
        List<object> parameters,
        DbCommand command,
        NestedSetTreeRegistryMapping mapping,
        INestedSetTreeLockRequest[] requests
    )
    {
        // WHY: Stable ordinals align mapped parameters and lifecycle requirements without allocating row objects.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < requests.Length; index++)
        {
            if (index != 0)
            {
                rows.Append(", ");
            }

            var request = requests[index];
            rows.Append('(');

            if (mapping.Scope is not null)
            {
                AppendParameter(rows, parameters, command, mapping.Scope, request.ScopeValue, "scope" + index);
                rows.Append(", ");
            }

            AppendParameter(rows, parameters, command, mapping.TreeId, request.TreeIdValue, "treeId" + index);
            rows.Append(", ").Append(index).Append(", ").Append((int)request.Mode).Append(')');
        }
    }

    /// <summary>Serializes provider-form values directly into one rowset parameter.</summary>
    internal static void AddJsonParameter(
        List<object> parameters,
        DbCommand command,
        NestedSetTreeRegistryMapping mapping,
        INestedSetTreeLockRequest[] requests,
        bool hexBinary
    )
    {
        var buffer = new ArrayBufferWriter<byte>(Math.Max(256, Math.Min(requests.Length, 1024) * 64));
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartArray();

            // WHY: Persist the request ordinal alongside each JSON row for exact lock-plan correlation.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < requests.Length; index++)
            {
                var request = requests[index];
                json.WriteStartObject();

                if (mapping.Scope is not null)
                {
                    json.WritePropertyName("s");
                    WriteProviderValue(json, mapping.Scope, request.ScopeValue, hexBinary);
                }

                json.WritePropertyName("t");
                WriteProviderValue(json, mapping.TreeId, request.TreeIdValue, hexBinary);
                json.WriteNumber("o", index);
                json.WriteNumber("c", (int)request.Mode);
                json.WriteEndObject();
            }

            json.WriteEndArray();
        }

        var parameter = command.CreateParameter();
        parameter.ParameterName = "requestedTrees";
        parameter.DbType = DbType.String;
        parameter.Size = -1;
        parameter.Value = Encoding.UTF8.GetString(buffer.WrittenSpan);
        parameters.Add(parameter);
    }

    /// <summary>Writes one provider-form identity value without allocating an intermediate row object.</summary>
    private static void WriteProviderValue(
        Utf8JsonWriter json,
        IProperty property,
        object? value,
        bool hexBinary
    )
    {
        var converter = property.GetRelationalTypeMapping().Converter;
        var providerValue = converter is null ? value : converter.ConvertToProvider(value);

        if (providerValue is null)
        {
            throw new InvalidOperationException($"Tree identity '{property.Name}' converted to null.");
        }

        if (hexBinary && providerValue is byte[] bytes)
        {
            json.WriteStringValue(Convert.ToHexString(bytes));

            return;
        }

        JsonSerializer.Serialize(json, providerValue, providerValue.GetType());
    }

    /// <summary>Appends one positional placeholder and a model-derived relational parameter.</summary>
    internal static void AppendParameter(
        StringBuilder sql,
        List<object> parameters,
        DbCommand command,
        IProperty property,
        object? value,
        string name
    )
    {
        sql
            .Append('{')
            .Append(parameters.Count)
            .Append('}');

        parameters.Add(
            property
                .GetRelationalTypeMapping()
                .CreateParameter(command, name, value, nullable: false));
    }
}

/// <summary>Groups already delimited column roles shared by native tree-rowset renderers.</summary>
/// <param name="Source">The rowset source alias.</param>
/// <param name="Scope">The optional scope-column alias.</param>
/// <param name="TreeId">The tree-identity alias.</param>
/// <param name="Ordinal">The original request ordinal alias.</param>
/// <param name="Create">The lifecycle request-mode alias.</param>
internal readonly record struct NestedSetTreeRowsetAliases(
    string Source,
    string Scope,
    string TreeId,
    string Ordinal,
    string Create
);

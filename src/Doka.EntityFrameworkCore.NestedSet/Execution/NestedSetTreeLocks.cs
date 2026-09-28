namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Acquires complete tree identities in deterministic database order.</summary>
internal static class NestedSetTreeLocks
{
    /// <summary>Switches wide requests to one rowset parameter before scalar SQL becomes expensive.</summary>
    private const int JsonRowsetThreshold = 256;

    /// <summary>Rejects input identities that the database considers equal under mapped store semantics.</summary>
    /// <param name="context">The context whose active transaction owns the validation snapshot.</param>
    /// <param name="requests">The complete tree identities supplied by one forest import.</param>
    /// <param name="cancellationToken">The token used for database-native comparison.</param>
    /// <returns>A task that completes when every supplied identity is distinct.</returns>
    internal static async Task RequireDistinctAsync(
        DbContext context,
        IReadOnlyList<INestedSetTreeLockRequest> requests,
        CancellationToken cancellationToken
    )
    {
        var provider = NestedSetProviderCapabilities.Resolve(context);
        var groups = requests.GroupBy(request => request.Mapping.Registry);

        foreach (var group in groups)
        {
            var members = group.ToArray();
            var ordinals = await ResolveOrderAsync(context, members, provider, cancellationToken)
                .ConfigureAwait(false);

            if (ordinals.Count != members.Length)
            {
                throw new NestedSetException(
                    NestedSetErrorCode.InvalidImport,
                    "Every imported root must have a distinct TreeId within its Scope.");
            }
        }
    }

    /// <summary>Locks distinct requested trees without using CLR hashing or application comparison semantics.</summary>
    /// <param name="context">The context whose current transaction owns every acquired lock.</param>
    /// <param name="requests">The complete tree identities participating in one atomic operation.</param>
    /// <param name="cancellationToken">The token used for native ordering and lock acquisition.</param>
    /// <returns>A task that completes after every active or newly reserved registry row is locked.</returns>
    internal static async Task AcquireAsync(
        DbContext context,
        IReadOnlyList<INestedSetTreeLockRequest> requests,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (requests.Count == 0)
        {
            return;
        }

        var provider = NestedSetProviderCapabilities.Resolve(context);
        var groups = requests
            .GroupBy(request => request.Mapping.Registry)
            .OrderBy(group => group.Key.GetSchema(), StringComparer.Ordinal)
            .ThenBy(group => group.Key.GetTableName(), StringComparer.Ordinal)
            .ThenBy(group => group.Key.Name, StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var members = group.ToArray();
            var ordinals = await ResolveOrderAsync(context, members, provider, cancellationToken)
                .ConfigureAwait(false);

            foreach (var ordinal in ordinals)
            {
                await new NestedSetTreeLock(context, provider, members[ordinal])
                    .AcquireAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    /// <summary>Lets the database deduplicate and sort keys under their configured native semantics.</summary>
    /// <param name="context">The context supplying SQL and parameter type mappings.</param>
    /// <param name="requests">Requests that share one typed registry table.</param>
    /// <param name="provider">The verified dialect used to render collations and rowsets.</param>
    /// <param name="cancellationToken">The token used for the order query.</param>
    /// <returns>Representative request ordinals in canonical registry-key order.</returns>
    private static async Task<List<int>> ResolveOrderAsync(
        DbContext context,
        INestedSetTreeLockRequest[] requests,
        NestedSetProviderCapabilities provider,
        CancellationToken cancellationToken
    )
    {
        if (requests.Length == 1)
        {
            return [0];
        }

        var mapping = requests[0].Mapping;
        var sql = context.GetService<ISqlGenerationHelper>();
        var scopeAlias = sql.DelimitIdentifier(NestedSetTreeRegistryMetadata.Scope);
        var treeAlias = sql.DelimitIdentifier(NestedSetTreeRegistryMetadata.TreeId);
        var ordinalAlias = sql.DelimitIdentifier("Ordinal");
        var createAlias = sql.DelimitIdentifier("Create");
        var valueAlias = sql.DelimitIdentifier("Value");
        var sourceAlias = sql.DelimitIdentifier("RequestedTrees");
        var scopeExpression = mapping.Scope is null ? null : "r." + scopeAlias;
        var treeExpression = "r." + treeAlias;

        if (mapping.SourceScope is { } scope
            && HasStringStore(scope))
        {
            scopeExpression = ApplyCollation(context, provider, sql, scopeExpression!, scope, mapping.Hierarchy);
        }

        if (HasStringStore(mapping.SourceTreeId))
        {
            treeExpression = ApplyCollation(
                context,
                provider,
                sql,
                treeExpression,
                mapping.SourceTreeId,
                mapping.Hierarchy);
        }

        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        var useJson = provider.Kind == NestedSetProviderKind.SqlServer || requests.Length >= JsonRowsetThreshold;

        var parameters = new List<object>(useJson ? 1 : requests.Length * (mapping.Scope is null ? 1 : 2));
        var rows = new StringBuilder();

        if (provider.Kind == NestedSetProviderKind.SqlServer)
        {
            AppendSqlServerJsonSource(
                rows,
                parameters,
                command,
                sourceAlias,
                scopeAlias,
                treeAlias,
                ordinalAlias,
                createAlias,
                mapping,
                requests);
        }
        else if (useJson)
        {
            AppendJsonSource(
                rows,
                parameters,
                command,
                provider.Kind,
                sourceAlias,
                scopeAlias,
                treeAlias,
                ordinalAlias,
                createAlias,
                mapping,
                requests);
        }
        else
        {
            for (var index = 0; index < requests.Length; index++)
            {
                if (index != 0)
                {
                    rows.Append(provider.Kind == NestedSetProviderKind.MySql ? " UNION ALL " : ", ");
                }

                var request = requests[index];

                if (provider.Kind == NestedSetProviderKind.MySql)
                {
                    rows.Append("SELECT ");

                    if (mapping.Scope is not null)
                    {
                        AppendParameter(rows, parameters, command, mapping.Scope, request.ScopeValue, "scope" + index);
                        rows
                            .Append(" AS ")
                            .Append(scopeAlias)
                            .Append(", ");
                    }

                    AppendParameter(rows, parameters, command, mapping.TreeId, request.TreeIdValue, "treeId" + index);
                    rows
                        .Append(" AS ")
                        .Append(treeAlias)
                        .Append(", ")
                        .Append(index)
                        .Append(" AS ")
                        .Append(ordinalAlias)
                        .Append(", ")
                        .Append((int)request.Mode)
                        .Append(" AS ")
                        .Append(createAlias);
                }
                else
                {
                    rows.Append('(');

                    if (mapping.Scope is not null)
                    {
                        AppendParameter(rows, parameters, command, mapping.Scope, request.ScopeValue, "scope" + index);
                        rows.Append(", ");
                    }

                    AppendParameter(rows, parameters, command, mapping.TreeId, request.TreeIdValue, "treeId" + index);
                    rows
                        .Append(", ")
                        .Append(index)
                        .Append(", ")
                        .Append((int)request.Mode);
                    rows.Append(')');
                }
            }
        }

        var names = mapping.Scope is null
            ? $"{treeAlias}, {ordinalAlias}, {createAlias}"
            : $"{scopeAlias}, {treeAlias}, {ordinalAlias}, {createAlias}";

        var identity = scopeExpression is null ? treeExpression : scopeExpression + ", " + treeExpression;

        var sourceStatement = rows.ToString();

        if (provider.Kind != NestedSetProviderKind.SqlServer
            && !useJson)
        {
            sourceStatement = BuildSourceCte(provider.Kind, sourceAlias, names, sourceStatement);
        }

        // WHY: Create participates in grouping so a mixed create/existing request never loses its stricter
        // reservation contract. Descending order tries that reservation before an existing-row lock.
        var statement = sourceStatement
            + $"SELECT MIN(r.{ordinalAlias}) AS {valueAlias} FROM {sourceAlias} AS r "
            + $"GROUP BY {identity}, r.{createAlias} "
            + $"ORDER BY {identity}, r.{createAlias} DESC";

        return await context
            .Database
            .SqlQueryRaw<int>(statement, parameters.ToArray())
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Builds one typed SQL Server rowset carried by a single JSON parameter.</summary>
    private static void AppendSqlServerJsonSource(
        StringBuilder sql,
        List<object> parameters,
        System.Data.Common.DbCommand command,
        string sourceAlias,
        string scopeAlias,
        string treeAlias,
        string ordinalAlias,
        string createAlias,
        NestedSetTreeRegistryMapping mapping,
        INestedSetTreeLockRequest[] requests
    )
    {
        AddJsonParameter(parameters, command, mapping, requests, false);

        // WHY: SQL Server has a hard 2,100-parameter ceiling. OPENJSON preserves one set-based database order
        // for an arbitrarily wide save while the explicit schema restores each registry key's store type.
        sql
            .Append("WITH ")
            .Append(sourceAlias)
            .Append(" AS (SELECT * FROM OPENJSON({0}) WITH (");

        if (mapping.Scope is not null)
        {
            AppendJsonColumn(sql, scopeAlias, mapping.Scope, "s");
            sql.Append(", ");
        }

        AppendJsonColumn(sql, treeAlias, mapping.TreeId, "t");
        sql
            .Append(", ")
            .Append(ordinalAlias)
            .Append(" int 'strict $.o'")
            .Append(", ")
            .Append(createAlias)
            .Append(" int 'strict $.c')) ");
    }

    /// <summary>Builds one database-native rowset from a single JSON parameter.</summary>
    private static void AppendJsonSource(
        StringBuilder sql,
        List<object> parameters,
        System.Data.Common.DbCommand command,
        NestedSetProviderKind kind,
        string sourceAlias,
        string scopeAlias,
        string treeAlias,
        string ordinalAlias,
        string createAlias,
        NestedSetTreeRegistryMapping mapping,
        INestedSetTreeLockRequest[] requests
    )
    {
        AddJsonParameter(parameters, command, mapping, requests, true);
        sql
            .Append("WITH ")
            .Append(sourceAlias)
            .Append(" AS (SELECT ");

        if (mapping.Scope is not null)
        {
            AppendJsonValue(sql, kind, mapping.Scope, "s")
                .Append(" AS ")
                .Append(scopeAlias)
                .Append(", ");
        }

        AppendJsonValue(sql, kind, mapping.TreeId, "t")
            .Append(" AS ")
            .Append(treeAlias);

        if (kind == NestedSetProviderKind.MySql)
        {
            sql
                .Append(", j.o AS ")
                .Append(ordinalAlias)
                .Append(", j.c AS ")
                .Append(createAlias)
                .Append(" FROM JSON_TABLE({0}, '$[*]' COLUMNS (");

            if (mapping.Scope is not null)
            {
                AppendMySqlJsonColumn(sql, mapping.Scope, "s");
                sql.Append(", ");
            }

            AppendMySqlJsonColumn(sql, mapping.TreeId, "t");
            sql
                .Append(", o INT PATH '$.o' ERROR ON EMPTY ERROR ON ERROR")
                .Append(", c INT PATH '$.c' ERROR ON EMPTY ERROR ON ERROR)) AS j) ");

            return;
        }

        if (kind == NestedSetProviderKind.Sqlite)
        {
            sql
                .Append(", CAST(json_extract(j.value, '$.o') AS INTEGER) AS ")
                .Append(ordinalAlias)
                .Append(", CAST(json_extract(j.value, '$.c') AS INTEGER) AS ")
                .Append(createAlias)
                .Append(" FROM json_each({0}) AS j) ");

            return;
        }

        sql
            .Append(", CAST(j.value->>'o' AS integer) AS ")
            .Append(ordinalAlias)
            .Append(", CAST(j.value->>'c' AS integer) AS ")
            .Append(createAlias)
            .Append(" FROM jsonb_array_elements(CAST({0} AS jsonb)) AS j(value)) ");
    }

    /// <summary>Appends a provider-typed identity expression extracted from one JSON row.</summary>
    private static StringBuilder AppendJsonValue(
        StringBuilder sql,
        NestedSetProviderKind kind,
        IProperty property,
        string path
    )
    {
        if (kind == NestedSetProviderKind.MySql)
        {
            return sql
                .Append("j.")
                .Append(path);
        }

        var expression = kind == NestedSetProviderKind.Sqlite
            ? "json_extract(j.value, '$." + path + "')"
            : "j.value->>'" + path + "'";

        var providerType = property.GetRelationalTypeMapping()
                .Converter?.ProviderClrType
            ?? property.GetRelationalTypeMapping()
                .ClrType;

        // WHY: Hex is an injective representation of binary identities. Text grouping preserves byte equality
        // without relying on provider-specific binary JSON coercion or allocating one SQL parameter per row.
        if (providerType == typeof(byte[]))
        {
            return sql.Append(expression);
        }

        return sql
            .Append("CAST(")
            .Append(expression)
            .Append(" AS ")
            .Append(
                property.GetRelationalTypeMapping()
                    .StoreType)
            .Append(')');
    }

    /// <summary>Declares one typed MySQL or MariaDB JSON_TABLE identity column.</summary>
    private static void AppendMySqlJsonColumn(
        StringBuilder sql,
        IProperty property,
        string path
    )
    {
        var providerType = property.GetRelationalTypeMapping().Converter?.ProviderClrType
            ?? property.GetRelationalTypeMapping().ClrType;

        var storeType = property.GetRelationalTypeMapping().StoreType;

        // WHY: Doka stores Guid values in binary(16) without an EF value converter. JSON_TABLE would
        // truncate the canonical Guid text to 16 bytes; CHAR(36) keeps its exact equality and stable order.
        var jsonType = providerType == typeof(Guid)
            && (storeType.StartsWith("binary", StringComparison.OrdinalIgnoreCase)
                || storeType.StartsWith("varbinary", StringComparison.OrdinalIgnoreCase))
                ? "CHAR(36)"
                : providerType == typeof(byte[])
                    ? "LONGTEXT"
                    : storeType;

        sql
            .Append(path)
            .Append(' ')
            .Append(jsonType)
            .Append(" PATH '$.")
            .Append(path)
            .Append("' ERROR ON EMPTY ERROR ON ERROR");
    }

    /// <summary>Serializes provider-form values directly into one rowset parameter.</summary>
    private static void AddJsonParameter(
        List<object> parameters,
        System.Data.Common.DbCommand command,
        NestedSetTreeRegistryMapping mapping,
        INestedSetTreeLockRequest[] requests,
        bool hexBinary
    )
    {
        var buffer = new ArrayBufferWriter<byte>(Math.Max(256, Math.Min(requests.Length, 1024) * 64));
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartArray();

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

    /// <summary>Appends one trusted model-derived OPENJSON column declaration.</summary>
    private static void AppendJsonColumn(
        StringBuilder sql,
        string alias,
        IProperty property,
        string path
    ) => sql
        .Append(alias)
        .Append(' ')
        .Append(property.GetRelationalTypeMapping().StoreType)
        .Append(" 'strict $.")
        .Append(path)
        .Append('\'');

    /// <summary>Builds the provider-specific common table expression for requested typed tree identities.</summary>
    /// <param name="kind">The verified provider dialect.</param>
    /// <param name="sourceAlias">The delimited common table expression alias.</param>
    /// <param name="names">The delimited column list.</param>
    /// <param name="rows">The parameterized row expressions.</param>
    /// <returns>The common table expression followed by one separating space.</returns>
    internal static string BuildSourceCte(
        NestedSetProviderKind kind,
        string sourceAlias,
        string names,
        string rows
    ) => kind == NestedSetProviderKind.MySql
        ? $"WITH {sourceAlias} ({names}) AS ({rows}) "
        : $"WITH {sourceAlias} ({names}) AS (VALUES {rows}) ";

    /// <summary>Appends one positional placeholder and a model-derived relational parameter.</summary>
    private static void AppendParameter(
        StringBuilder sql,
        List<object> parameters,
        System.Data.Common.DbCommand command,
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

    /// <summary>Applies the captured effective string collation to derived parameter values.</summary>
    /// <param name="context">The context owning the finalized hierarchy metadata.</param>
    /// <param name="provider">The verified provider whose collation syntax is used.</param>
    /// <param name="sql">The provider's SQL identifier service.</param>
    /// <param name="expression">The native request identity expression.</param>
    /// <param name="source">The source hierarchy identity property.</param>
    /// <param name="hierarchy">The configured owner, including a concrete inherited-property mapping.</param>
    /// <returns>The known source collation expression, or the original expression for an unknown default.</returns>
    private static string ApplyCollation(
        DbContext context,
        NestedSetProviderCapabilities provider,
        ISqlGenerationHelper sql,
        string expression,
        IProperty source,
        IEntityType hierarchy
    )
    {
        var collation = NestedSetCollations.Resolve(context, source, hierarchy);

        return collation is null ? expression : expression + " COLLATE " + provider.CollationSql(sql, collation);
    }

    /// <summary>Checks the finalized provider type used by a registry identity column.</summary>
    private static bool HasStringStore(
        IProperty property
    ) => (property.GetRelationalTypeMapping().Converter?.ProviderClrType ?? property.ClrType) == typeof(string);
}

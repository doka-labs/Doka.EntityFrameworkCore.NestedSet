namespace Doka.EntityFrameworkCore.NestedSet.MySql;

internal sealed partial class MySqlNestedSetProvider
{
    /// <inheritdoc />
    internal override string BuildTreeRowsetSource(
        DbCommand command,
        List<object> parameters,
        NestedSetTreeRowsetAliases aliases,
        NestedSetTreeRegistryMapping mapping,
        INestedSetTreeLockRequest[] requests
    )
    {
        if (requests.Length < NestedSetRegistryRowset.ScalarRowLimit)
        {
            return ScalarTreeRowset(command, parameters, aliases, mapping, requests);
        }

        NestedSetRegistryRowset.AddJsonParameter(parameters, command, mapping, requests, true);
        var rows = new StringBuilder();
        rows
            .Append("WITH ")
            .Append(aliases.Source)
            .Append(" AS (SELECT ");

        if (mapping.Scope is not null)
        {
            rows
                .Append("j.s AS ")
                .Append(aliases.Scope)
                .Append(", ");
        }

        rows
            .Append("j.t AS ")
            .Append(aliases.TreeId)
            .Append(", j.o AS ")
            .Append(aliases.Ordinal)
            .Append(", j.c AS ")
            .Append(aliases.Create)
            .Append(" FROM JSON_TABLE({0}, '$[*]' COLUMNS (");

        if (mapping.Scope is not null)
        {
            AppendJsonColumn(rows, mapping.Scope, "s");
            rows.Append(", ");
        }

        AppendJsonColumn(rows, mapping.TreeId, "t");
        rows
            .Append(", o INT PATH '$.o' ERROR ON EMPTY ERROR ON ERROR")
            .Append(", c INT PATH '$.c' ERROR ON EMPTY ERROR ON ERROR)) AS j) ");

        return rows.ToString();
    }

    /// <summary>Builds mapped scalar SELECT rows without relying on VALUES table syntax.</summary>
    private static string ScalarTreeRowset(
        DbCommand command,
        List<object> parameters,
        NestedSetTreeRowsetAliases aliases,
        NestedSetTreeRegistryMapping mapping,
        INestedSetTreeLockRequest[] requests
    )
    {
        var rows = new StringBuilder();

        // WHY: Original request ordinals align every row's aliases, parameters and lifecycle requirement.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < requests.Length; index++)
        {
            if (index != 0)
            {
                rows.Append(" UNION ALL ");
            }

            rows.Append("SELECT ");
            var request = requests[index];

            if (mapping.Scope is not null)
            {
                NestedSetRegistryRowset.AppendParameter(
                    rows,
                    parameters,
                    command,
                    mapping.Scope,
                    request.ScopeValue,
                    "scope" + index);
                rows
                    .Append(" AS ")
                    .Append(aliases.Scope)
                    .Append(", ");
            }

            NestedSetRegistryRowset.AppendParameter(
                rows,
                parameters,
                command,
                mapping.TreeId,
                request.TreeIdValue,
                "treeId" + index);
            rows
                .Append(" AS ")
                .Append(aliases.TreeId)
                .Append(", ")
                .Append(index)
                .Append(" AS ")
                .Append(aliases.Ordinal)
                .Append(", ")
                .Append((int)request.Mode)
                .Append(" AS ")
                .Append(aliases.Create);
        }

        var names = mapping.Scope is null
            ? $"{aliases.TreeId}, {aliases.Ordinal}, {aliases.Create}"
            : $"{aliases.Scope}, {aliases.TreeId}, {aliases.Ordinal}, {aliases.Create}";

        return $"WITH {aliases.Source} ({names}) AS ({rows}) ";
    }

    /// <summary>Declares one typed MySQL or MariaDB JSON_TABLE identity column.</summary>
    private static void AppendJsonColumn(
        StringBuilder sql,
        IProperty property,
        string path
    )
    {
        var providerType = property.GetRelationalTypeMapping()
                .Converter?.ProviderClrType
            ?? property.GetRelationalTypeMapping()
                .ClrType;

        var storeType = property.GetRelationalTypeMapping()
            .StoreType;

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
}

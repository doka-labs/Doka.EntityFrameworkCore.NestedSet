namespace Doka.EntityFrameworkCore.NestedSet.Sqlite;

internal sealed partial class SqliteNestedSetProvider
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
            AppendJsonValue(rows, mapping.Scope, "s")
                .Append(" AS ")
                .Append(aliases.Scope)
                .Append(", ");
        }

        AppendJsonValue(rows, mapping.TreeId, "t")
            .Append(" AS ")
            .Append(aliases.TreeId);
        rows
            .Append(", CAST(json_extract(j.value, '$.o') AS INTEGER) AS ")
            .Append(aliases.Ordinal)
            .Append(", CAST(json_extract(j.value, '$.c') AS INTEGER) AS ")
            .Append(aliases.Create)
            .Append(" FROM json_each({0}) AS j) ");

        return rows.ToString();
    }

    /// <summary>Restores mapped identity types while retaining injective binary hexadecimal values.</summary>
    private static StringBuilder AppendJsonValue(
        StringBuilder sql,
        IProperty property,
        string path
    )
    {
        var expression = "json_extract(j.value, '$." + path + "')";
        var mapping = property.GetRelationalTypeMapping();
        var providerType = mapping.Converter?.ProviderClrType ?? mapping.ClrType;

        // WHY: Text grouping of injective hexadecimal values preserves byte equality without native JSON coercion.
        if (providerType == typeof(byte[]))
        {
            return sql.Append(expression);
        }

        return sql
            .Append("CAST(")
            .Append(expression)
            .Append(" AS ")
            .Append(mapping.StoreType)
            .Append(')');
    }

    /// <summary>Builds a bounded scalar source using mapped values and original request ordinals.</summary>
    private static string ScalarTreeRowset(
        DbCommand command,
        List<object> parameters,
        NestedSetTreeRowsetAliases aliases,
        NestedSetTreeRegistryMapping mapping,
        INestedSetTreeLockRequest[] requests
    )
    {
        var rows = new StringBuilder();

        NestedSetRegistryRowset.AppendScalarRows(rows, parameters, command, mapping, requests);

        var names = mapping.Scope is null
            ? $"{aliases.TreeId}, {aliases.Ordinal}, {aliases.Create}"
            : $"{aliases.Scope}, {aliases.TreeId}, {aliases.Ordinal}, {aliases.Create}";

        return $"WITH {aliases.Source} ({names}) AS (VALUES {rows}) ";
    }
}

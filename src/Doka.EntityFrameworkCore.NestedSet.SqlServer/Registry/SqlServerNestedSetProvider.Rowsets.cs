namespace Doka.EntityFrameworkCore.NestedSet.SqlServer;

internal sealed partial class SqlServerNestedSetProvider
{
    /// <inheritdoc />
    internal override int TreeRowsetParameterCount(
        int requestCount,
        bool scoped
    ) => 1;

    /// <inheritdoc />
    internal override string BuildTreeRowsetSource(
        DbCommand command,
        List<object> parameters,
        NestedSetTreeRowsetAliases aliases,
        NestedSetTreeRegistryMapping mapping,
        INestedSetTreeLockRequest[] requests
    )
    {
        NestedSetRegistryRowset.AddJsonParameter(parameters, command, mapping, requests, false);
        var rows = new StringBuilder();

        // WHY: The hard scalar-parameter ceiling must not fragment canonical database lock ordering.
        // An explicit OPENJSON schema restores each key's mapped type from the single rowset parameter.
        rows
            .Append("WITH ")
            .Append(aliases.Source)
            .Append(" AS (SELECT * FROM OPENJSON({0}) WITH (");

        if (mapping.Scope is not null)
        {
            AppendJsonColumn(rows, aliases.Scope, mapping.Scope, "s");
            rows.Append(", ");
        }

        AppendJsonColumn(rows, aliases.TreeId, mapping.TreeId, "t");
        rows
            .Append(", ")
            .Append(aliases.Ordinal)
            .Append(" int 'strict $.o'")
            .Append(", ")
            .Append(aliases.Create)
            .Append(" int 'strict $.c')) ");

        return rows.ToString();
    }

    /// <summary>Uses only delimited aliases and trusted relational store types in the native JSON schema.</summary>
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
}

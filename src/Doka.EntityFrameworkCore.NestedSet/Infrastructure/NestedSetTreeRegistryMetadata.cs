namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Defines stable model and store names for the internal typed tree registry.</summary>
internal static class NestedSetTreeRegistryMetadata
{
    /// <summary>The optional application partition column.</summary>
    internal const string Scope = "Scope";

    /// <summary>The stable tree identity column.</summary>
    internal const string TreeId = "TreeId";

    /// <summary>The monotonically increasing structural revision column.</summary>
    internal const string Revision = "Revision";

    /// <summary>The active or tombstoned lifecycle column.</summary>
    internal const string Lifecycle = "Lifecycle";

    /// <summary>The persisted value for a tree that may accept structural operations.</summary>
    internal const byte Active = 0;

    /// <summary>The persisted value reserving a deleted tree identity against silent reuse.</summary>
    internal const byte Tombstoned = 1;

    /// <summary>Builds the unique shared-entity name stored on the hierarchy metadata.</summary>
    /// <param name="entityName">The finalized EF entity-type name.</param>
    /// <returns>A stable model name unique to one configured hierarchy.</returns>
    internal static string EntityName(
        string entityName
    ) => "Doka.EntityFrameworkCore.NestedSet.TreeRegistry:" + entityName;

    /// <summary>Builds a provider-portable table name without exposing application identifiers.</summary>
    /// <param name="schema">The optional physical schema containing the hierarchy table.</param>
    /// <param name="tableName">The physical hierarchy table name.</param>
    /// <param name="nodeKeyColumn">The physical node-key column.</param>
    /// <param name="scopeColumn">The optional physical scope column.</param>
    /// <param name="treeIdColumn">The physical tree-identity column.</param>
    /// <returns>A deterministic table name below the shortest supported identifier limit.</returns>
    internal static string TableName(
        string? schema,
        string tableName,
        string nodeKeyColumn,
        string? scopeColumn,
        string treeIdColumn
    )
    {
        // WHY: CLR renames must not rename a persisted registry. Physical identity columns also distinguish
        // separate hierarchy mappings that share a table through EF table sharing.
        var storeIdentity = string.Join(
            "\0",
            schema is null ? "0" : "1" + schema,
            tableName,
            nodeKeyColumn,
            scopeColumn is null ? "0" : "1" + scopeColumn,
            treeIdColumn);

        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(storeIdentity)))[..16];

        return "DokaNestedSetTrees_" + digest;
    }
}

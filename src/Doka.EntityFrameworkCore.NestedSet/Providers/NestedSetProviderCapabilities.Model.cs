namespace Doka.EntityFrameworkCore.NestedSet.Providers;

internal abstract partial class NestedSetProviderCapabilities
{
    /// <summary>Gets the provider's identifier length limit, or null when it documents none.</summary>
    internal virtual int? MaxIdentifierLength => null;

    /// <summary>Gets whether the identifier limit counts UTF-8 bytes rather than characters.</summary>
    internal virtual bool IdentifierLengthCountsBytes => false;

    /// <summary>Gets the comparison under which two quoted table or schema names can denote the same object.</summary>
    /// <remarks>
    ///     Defaults to case-insensitive. MySQL and MariaDB decide this through lower_case_table_names and SQL Server
    ///     through the database collation. A model cannot see the server's setting, so it must not rely on a
    ///     case-only difference that would collide there.
    /// </remarks>
    internal virtual StringComparer IdentifierComparer => StringComparer.OrdinalIgnoreCase;

    /// <summary>Gets whether a configured schema becomes part of the physical table identity.</summary>
    internal virtual bool HonorsSchemas => true;

    /// <summary>Recognizes a name the provider reserves for something other than an ordinary table.</summary>
    /// <param name="name">The candidate physical table name.</param>
    /// <returns>Whether an ordinary table cannot use this name.</returns>
    internal virtual bool IsReservedTableName(
        string name
    ) => false;

    /// <summary>Converts a guarded Boolean predicate into the native generated-column expression.</summary>
    internal virtual string AncestorFlagSql(
        string predicate
    ) => predicate;

    /// <summary>Gets whether the native generated ancestor flag requires stored persistence.</summary>
    internal virtual bool StoresAncestorFlag => false;

    /// <summary>Adds native finalizers after common relationship and structural-index reconciliation.</summary>
    /// <param name="conventions">The convention set used to finalize the configured model.</param>
    /// <param name="sql">The actual provider helper available to native SQL annotations.</param>
    internal virtual void AddModelFinalizers(
        ConventionSet conventions,
        ISqlGenerationHelper sql
    ) { }

    /// <summary>Resolves a supported native table default after physical-column collation overrides.</summary>
    /// <param name="model">The model containing native table-level collation annotations.</param>
    /// <param name="store">The exact mapped table whose default collation is required.</param>
    /// <returns>The supported native default, or null when no table-level default is available.</returns>
    internal virtual string? ResolveTableCollation(
        IReadOnlyModel model,
        StoreObjectIdentifier store
    ) => null;

    /// <summary>Recognizes exact native bounded-character mappings whose limits count Unicode scalars.</summary>
    /// <param name="mapping">The effective, possibly converter-composed relational mapping.</param>
    /// <returns>Whether this concrete mapping uses native character counts for finite identity limits.</returns>
    internal virtual bool UsesUnicodeScalarLength(
        RelationalTypeMapping mapping
    ) => false;

    /// <summary>Recognizes native annotations that preserve the complete ordinary structural index path.</summary>
    internal virtual bool IsCompatibleIndexAnnotation(
        IConventionAnnotation annotation
    ) => false;
}

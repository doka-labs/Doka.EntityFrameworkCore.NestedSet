namespace Doka.EntityFrameworkCore.NestedSet.Execution;

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

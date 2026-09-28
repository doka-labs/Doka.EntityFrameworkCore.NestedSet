namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Marks an internal tree-registry row in generated and compiled Entity Framework models.</summary>
/// <remarks>
///     Applications do not create, update, or delete instances of this type. NestedSet maps one named shared
///     entity type per configured hierarchy and stores its typed Scope and TreeId values in shadow properties.
///     Public visibility is required because EF-generated compiled models live in the consuming assembly.
/// </remarks>
public sealed class NestedSetTreeRegistry
{
    /// <summary>Creates an infrastructure row for EF materialization and compiled-model construction.</summary>
    public NestedSetTreeRegistry() { }
}

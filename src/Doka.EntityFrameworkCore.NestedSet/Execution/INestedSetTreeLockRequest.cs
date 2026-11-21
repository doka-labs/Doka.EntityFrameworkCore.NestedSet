namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Exposes only the metadata and erased values required by heterogeneous native registry operations.</summary>
internal interface INestedSetTreeLockRequest
{
    /// <summary>Gets the exact cached registry mapping shared by hierarchy aliases.</summary>
    NestedSetTreeRegistryMapping Mapping { get; }

    /// <summary>Gets the Scope at the native parameter boundary, or null only when no Scope is mapped.</summary>
    object? ScopeValue { get; }

    /// <summary>Gets the non-null TreeId at the native parameter boundary.</summary>
    object TreeIdValue { get; }

    /// <summary>Gets the required registry lifecycle transition.</summary>
    NestedSetTreeLockMode Mode { get; }
}

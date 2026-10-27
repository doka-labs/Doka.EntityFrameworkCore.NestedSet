namespace Doka.EntityFrameworkCore.NestedSet.Mapping;

/// <summary>Defines the stable EF metadata namespace, structural roles, ordering, and registry keys.</summary>
internal static class NestedSetAnnotationNames
{
    /// <summary>Prefixes every nested-set annotation, including each structural role.</summary>
    internal const string Prefix = "Doka:NestedSet:";

    /// <summary>Stores the optional generated nonleaf property used by the additive ancestor index.</summary>
    internal const string AncestorIndexProperty = Prefix + "AncestorIndexProperty";

    /// <summary>Stores configured sibling-order property names in precedence order.</summary>
    internal const string OrderProperties = Prefix + "OrderProperties";

    /// <summary>Stores the direction of each configured sibling-order property.</summary>
    internal const string OrderDescending = Prefix + "OrderDescending";

    /// <summary>Stores the explicit null placement for every configured sibling-order property.</summary>
    internal const string OrderNullSort = Prefix + "OrderNullSort";

    /// <summary>Records that the finalizing convention captured runtime-safe effective string collations.</summary>
    internal const string CollationsCaptured = Prefix + "CollationsCaptured";

    /// <summary>Captures the native Scope character-count policy without retaining provider services.</summary>
    internal const string ScopeUnicodeScalarLength = Prefix + "ScopeUnicodeScalarLength";

    /// <summary>Captures the native TreeId character-count policy for runtime and compiled models.</summary>
    internal const string TreeIdUnicodeScalarLength = Prefix + "TreeIdUnicodeScalarLength";

    /// <summary>Prefixes hierarchy-owned property collations; an empty value means an unknown default.</summary>
    internal const string Collation = Prefix + "Collation";

    /// <summary>Stores the integer value of the configured sibling-order placement policy.</summary>
    internal const string OrderMode = Prefix + "OrderMode";

    /// <summary>Records the policy that structural writes preserve application-managed concurrency tokens.</summary>
    internal const string PreserveApplicationConcurrencyTokens = Prefix + "PreserveApplicationConcurrencyTokens";

    /// <summary>Stores the named shared entity type backing one hierarchy's typed tree registry.</summary>
    internal const string TreeRegistryEntity = Prefix + "TreeRegistryEntity";

    /// <summary>Marks a named shared entity type as library-owned tree-registry infrastructure.</summary>
    internal const string TreeRegistryOwner = Prefix + "TreeRegistryOwner";
}

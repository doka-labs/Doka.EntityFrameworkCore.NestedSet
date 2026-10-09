namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Shares EF's exact CLR setters without creating entries in an application's state manager.</summary>
internal static class NestedSetDetachedValueSetter
{
    private static readonly ConditionalWeakTable<IPropertyBase, IClrPropertySetter> s_setters = new();

    /// <summary>Gets a model-scoped CLR setter, or no setter when the detached input has no CLR storage.</summary>
    /// <param name="property">The finalized scalar, complex leaf, or owned reference navigation metadata.</param>
    /// <returns>The compiled setter shared with EF, or <see langword="null" /> for a shadow property.</returns>
    internal static IClrPropertySetter? Get(
        IPropertyBase property
    ) => property.IsShadowProperty()
        // WHY: Imported inputs leave the tracker after each payload batch. Shadow state has no observable
        // detached CLR representation; persisted values remain available through the public query API.
        ? null
        : s_setters.GetValue(property, CreateSetter);

    /// <summary>Obtains the metadata-owned compiled setter once, preserving the original framework exception.</summary>
    private static IClrPropertySetter CreateSetter(
        IPropertyBase property
    ) =>
        // WHY: IClrPropertySetter is a public extension contract, but EF exposes its acquisition only through
        // IRuntimePropertyBase. This metadata-only seam avoids duplicating EF's field, indexer, proxy and complex
        // value-copyback logic. Reflection stays outside the row loop and neither cache captures a context.
        NestedSetInsertionContract.Instance.Setter(property);
}

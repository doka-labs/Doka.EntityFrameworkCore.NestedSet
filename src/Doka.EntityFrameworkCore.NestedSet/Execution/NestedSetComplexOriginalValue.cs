namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Restores captured complex originals without detecting a new payload change.</summary>
internal static class NestedSetComplexOriginalValue
{
    private static readonly Action<object, IPropertyBase, object?> s_restore = Bind();

    /// <summary>Reinstates the saved original after a null-container property's public flag reset.</summary>
    /// <param name="entry">The complex leaf whose original storage already exists.</param>
    /// <param name="value">The independent original value retained before the temporary alignment.</param>
    internal static void Restore(
        PropertyEntry entry,
        object? value
    ) => Restore(NestedSetComplexEntryAccess.Owner(entry), entry.Metadata, value);

    /// <summary>Restores a scalar or collection original on the exact retained native containing entry.</summary>
    /// <param name="owner">The native entity or complex element whose original buffer owns the property.</param>
    /// <param name="metadata">The model-owned scalar or complex collection descriptor.</param>
    /// <param name="value">The captured original, cloned through EF's configured snapshot behavior.</param>
    internal static void Restore(
        object owner,
        IPropertyBase metadata,
        object? value
    ) => s_restore(owner, metadata, value);

    /// <summary>Closes the recovery-only EF original-buffer seam once, without touching native state flags.</summary>
    private static Action<object, IPropertyBase, object?> Bind()
    {
        var assembly = typeof(IUpdateEntry).Assembly;
        var nativeType = assembly.GetType("Microsoft.EntityFrameworkCore.ChangeTracking.Internal.InternalEntryBase")
            ?? throw new InvalidOperationException("EF Core does not expose the expected native entry base.");

        var originals = nativeType.GetField("_originalValues", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("EF Core does not expose the expected original-value buffer.");

        var setter = originals.FieldType.GetMethod("SetValue", [typeof(IPropertyBase), typeof(object), typeof(int)])
            ?? throw new InvalidOperationException("EF Core does not expose the expected original-value setter.");

        var initialize = nativeType.GetMethod("EnsureOriginalValues", Type.EmptyTypes)
            ?? throw new InvalidOperationException("EF Core does not expose the expected original-buffer initializer.");

        if (setter.ReturnType != typeof(void)
            || setter.IsStatic
            || !originals.FieldType.IsValueType
            || initialize.ReturnType != typeof(void)
            || initialize.IsStatic)
        {
            throw new InvalidOperationException("EF Core's original-value recovery signature is incompatible.");
        }

        // WHY: The public OriginalValue setter runs DetectValueChange even with automatic detection disabled.
        // Its original-buffer writer supplies comparer cloning without touching CLR values, flags or state.
        // This seam runs only after rollback; public flag setters retain EF's normal state notifications.
        var entry = Expression.Parameter(typeof(object), "entry");
        var metadata = Expression.Parameter(typeof(IPropertyBase), "metadata");
        var value = Expression.Parameter(typeof(object), "value");
        var owner = Expression.Convert(entry, nativeType);

        return Expression
            .Lambda<Action<object, IPropertyBase, object?>>(
                Expression.Block(
                    Expression.Call(owner, initialize),
                    Expression.Call(
                        Expression.Field(owner, originals),
                        setter,
                        metadata,
                        value,
                        Expression.Constant(-1))),
                entry,
                metadata,
                value)
            .Compile();
    }
}

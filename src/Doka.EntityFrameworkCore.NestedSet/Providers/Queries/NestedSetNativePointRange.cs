namespace Doka.EntityFrameworkCore.NestedSet.Providers;

/// <summary>Marks a native closed point interval without requiring CLR ordering operators.</summary>
/// <typeparam name="TValue">The exact translated operand type, including converted identities.</typeparam>
internal static class NestedSetNativePointRange<TValue>
{
    /// <summary>Retains a closed SQL interval around one native mapped point.</summary>
    /// <param name="value">The mapped candidate column.</param>
    /// <param name="point">The source-derived point using the column's mapping.</param>
    /// <returns>The database comparison result after translation.</returns>
    internal static bool Contains(
        TValue value,
        TValue point
    ) => throw new InvalidOperationException("Native point ranges are supported only in translated queries.");
}

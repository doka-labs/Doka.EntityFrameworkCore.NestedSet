namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Preserves exact native identities throughout library-owned single and bounded bulk insertions.</summary>
internal static partial class NestedSetInsertionTracking
{
    private static readonly ConditionalWeakTable<IEntityType, IKey[]> s_keys = new();
    private static readonly ConditionalWeakTable<IEntityType, IForeignKey[]> s_foreignKeys = new();
    private static readonly ConditionalWeakTable<IProperty, ValueReader> s_readers = new();
    private static readonly ConditionalWeakTable<IKey, KeyShape> s_shapes = new();
    private static NestedSetInsertionContract Contract => NestedSetInsertionContract.Instance;

    /// <summary>Captures bounded identities and separates mutable native map keys from live CLR values.</summary>
    /// <param name="entry">The exact insertion-owned entry after its successful Added transition.</param>
    /// <returns>The bounded lifecycle handle used before callbacks and during detachment.</returns>
    internal static Identity Capture(
        EntityEntry entry
    ) => new(entry);

    /// <summary>Obtains EF's exact invariant entry identity without inspecting private storage fields.</summary>
    /// <param name="entry">A public wrapper for an introduced root or owned dependent.</param>
    /// <returns>The context-local identity shared by every wrapper for this exact mapped entry.</returns>
    internal static IUpdateEntry EntryIdentity(
        EntityEntry entry
    ) => Contract.Entry(entry);

    /// <summary>Obtains the exact context-local native manager for the introduced entry.</summary>
    private static object StateManager(
        IUpdateEntry entry
    ) => Contract.Manager(entry);
}

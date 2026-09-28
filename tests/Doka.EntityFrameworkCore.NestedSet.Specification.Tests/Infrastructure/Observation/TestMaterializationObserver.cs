namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>
///     Routes singleton materialization callbacks to context-specific observers without retaining contexts.
/// </summary>
internal sealed class TestMaterializationObserver : IMaterializationInterceptor
{
    private readonly ConditionalWeakTable<DbContext, Action<object>> _observers = new();

    /// <summary>Gets the shared interceptor identity used by every observed context's EF service provider.</summary>
    internal static TestMaterializationObserver Instance { get; } = new();

    /// <summary>Prevents per-test instances from growing EF's internal service-provider cache.</summary>
    private TestMaterializationObserver() { }

    /// <summary>Associates a context with its test-owned observer for the lifetime of that context.</summary>
    /// <param name="context">The context whose entity materialization should be counted.</param>
    /// <param name="observer">The callback recording materialization for this context only.</param>
    internal void Register(
        DbContext context,
        Action<object> observer
    ) =>
        // WHY: Materialization interceptors are EF singletons; a weak association keeps counters local without
        // creating a new internal service provider or retaining disposed contexts for each test.
        _observers.Add(context, observer);

    /// <inheritdoc />
    public object InitializedInstance(
        MaterializationInterceptionData materializationData,
        object entity
    )
    {
        if (_observers.TryGetValue(materializationData.Context, out var observer))
        {
            observer(entity);
        }

        return entity;
    }
}

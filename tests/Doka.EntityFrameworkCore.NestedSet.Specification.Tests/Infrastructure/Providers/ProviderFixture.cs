namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Binds one resource and its asynchronous lifetime to an immutable provider identity.</summary>
/// <typeparam name="TResource">The existing resource implementation, constructed once by this fixture.</typeparam>
/// <typeparam name="TEngine">The engine selected by the concrete provider suite.</typeparam>
public sealed class ProviderFixture<TResource, TEngine> : IProviderFixture<TResource>, IAsyncLifetime
    where TResource : class, new()
    where TEngine : struct, IProviderEngine
{
    /// <inheritdoc />
    public ValueTask InitializeAsync() => Value is IAsyncLifetime lifetime
        ? lifetime.InitializeAsync()
        : ValueTask.CompletedTask;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // WHY: Existing resources own their databases/files; xUnit must dispose that exact resource once.
        if (Value is IAsyncDisposable asynchronous)
        {
            await asynchronous.DisposeAsync();
        }
        else if (Value is IDisposable synchronous)
        {
            synchronous.Dispose();
        }
    }

    /// <inheritdoc />
    public string Engine => TEngine.Name;

    /// <inheritdoc />
    public TResource Value { get; } = new();
}

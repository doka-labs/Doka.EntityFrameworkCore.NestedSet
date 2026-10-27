namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Owns real provider services for independent metadata-only unit-test models.</summary>
/// <typeparam name="TContext">The exact context whose typed options retain the test's provider conventions.</typeparam>
internal sealed class OwnedModelTestServices<TContext> : IDisposable, IAsyncDisposable
    where TContext : DbContext
{
    private readonly ServiceProvider _provider;

    /// <summary>Gets typed options explicitly bound to this owner's provider graph.</summary>
    internal DbContextOptions<TContext> Options { get; }

    /// <summary>Retains the completed provider and the caller's real configuration.</summary>
    private OwnedModelTestServices(
        ServiceProvider provider,
        DbContextOptions<TContext> options
    )
    {
        _provider = provider;
        Options = new DbContextOptionsBuilder<TContext>(options).UseInternalServiceProvider(provider).Options;
    }

    /// <summary>Builds a disposable provider graph without adding intentional variants to EF's global cache.</summary>
    /// <param name="options">The actual provider, warning policy, and nested-set registrations under test.</param>
    /// <param name="configure">An actual test-owned service replacement, when the model requires one.</param>
    /// <returns>The provider owner passed to the test context before its base constructor runs.</returns>
    internal static OwnedModelTestServices<TContext> Create(
        DbContextOptions<TContext> options,
        Action<IServiceCollection>? configure = null
    )
    {
        var services = new ServiceCollection();

        // WHY: Cross-provider and model variants are deliberate test inputs. Retaining them in the
        // global cache made an unrelated design-time factory fail once its normal warning limit was reached.
        foreach (var extension in options.Extensions)
        {
            extension.ApplyServices(services);
        }

        // WHY: EF forbids ReplaceService options with an externally owned provider. Register the same
        // real replacement in that provider rather than dropping its model-identity semantics.
        configure?.Invoke(services);

        return new OwnedModelTestServices<TContext>(services.BuildServiceProvider(), options);
    }

    /// <inheritdoc />
    public void Dispose() => _provider.Dispose();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _provider.DisposeAsync();
}

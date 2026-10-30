namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Owns ordinary provider services for tests that require controlled cache residency.</summary>
internal static class CacheTestServices
{
    /// <summary>Creates a disposable graph with the actual provider, adapter and bounded EF cache.</summary>
    /// <param name="options">The experiment's configured provider and selected adapter options.</param>
    /// <returns>The graph shared by warm and measured contexts and disposed after both.</returns>
    internal static ServiceProvider Create(
        IDbContextOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        var services = new ServiceCollection();

        // WHY: An external service graph must include the exact selected adapter and its plugins.
        // Reconstructing only native services loses that owner and changes the production query pipeline.
        foreach (var extension in options.Extensions)
        {
            extension.ApplyServices(services);
        }

        // WHY: Unrelated suite queries can evict a just-warmed shape from EF's shared model/query cache.
        // Explicit ownership isolates the experiment without changing normal cache limits or retaining global graphs.
        return services.BuildServiceProvider();
    }
}

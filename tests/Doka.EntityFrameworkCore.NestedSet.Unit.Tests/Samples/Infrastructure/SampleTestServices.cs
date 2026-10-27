namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

/// <summary>Owns the actual provider services used by sample configuration and migration test contexts.</summary>
internal static class SampleTestServices
{
    /// <summary>Registers the sample's public extensions without using EF's global cache.</summary>
    /// <param name="options">The real sample provider and NestedSet registrations under test.</param>
    /// <returns>Services the caller disposes after its explicitly constructed context.</returns>
    internal static ServiceProvider Create(DbContextOptions options)
    {
        var services = new ServiceCollection();

        // WHY: Provider/version and read-only variants are intentional in these sample test families.
        // An owned graph keeps their normal warning behavior independent of unrelated test execution order.
        foreach (var extension in options.Extensions)
        {
            extension.ApplyServices(services);
        }

        return services.BuildServiceProvider();
    }
}

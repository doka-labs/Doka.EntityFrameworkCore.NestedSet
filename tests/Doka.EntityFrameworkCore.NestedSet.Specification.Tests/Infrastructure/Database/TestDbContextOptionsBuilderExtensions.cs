namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Provides diagnostics shared by intentionally diverse EF Core test service graphs.</summary>
internal static class TestDbContextOptionsBuilderExtensions
{
    /// <summary>Disables the service-provider-count heuristic for an untyped test options builder.</summary>
    /// <param name="builder">The test options builder to configure.</param>
    /// <returns>The original builder.</returns>
    internal static DbContextOptionsBuilder ConfigureTestWarnings(
        this DbContextOptionsBuilder builder
    )
    {
        // WHY: The integration assembly intentionally combines five providers, compiled models, model variants,
        // and per-scenario interceptors. Those legitimate graphs exceed EF's application-oriented threshold.
        builder.ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));

        return builder;
    }

    /// <summary>Disables the service-provider-count heuristic for a typed test options builder.</summary>
    /// <typeparam name="TContext">The context type configured by the builder.</typeparam>
    /// <param name="builder">The test options builder to configure.</param>
    /// <returns>The original typed builder.</returns>
    internal static DbContextOptionsBuilder<TContext> ConfigureTestWarnings<TContext>(
        this DbContextOptionsBuilder<TContext> builder
    )
        where TContext : DbContext
    {
        ((DbContextOptionsBuilder)builder).ConfigureTestWarnings();

        return builder;
    }
}

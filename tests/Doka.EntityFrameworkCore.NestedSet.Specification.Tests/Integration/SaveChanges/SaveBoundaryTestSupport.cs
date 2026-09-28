namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Builds save-boundary options from the exact initialized fixture provider.</summary>
internal static class SaveBoundaryTestSupport
{
    /// <summary>
    ///     Creates typed options while retaining the fixture's exact provider and connection configuration.
    /// </summary>
    /// <param name="context">The initialized fixture context whose database will be reused.</param>
    /// <param name="interceptors">Observers scoped to this test boundary.</param>
    /// <returns>Options suitable for both ordinary construction and the public pooled context factory.</returns>
    internal static DbContextOptionsBuilder<SaveBoundaryContext> Options(
        DbContext context,
        params IInterceptor[] interceptors
    )
    {
        var extensions = context
            .GetService<IDbContextOptions>()
            .Extensions
            .ToDictionary(extension => extension.GetType());

        return new DbContextOptionsBuilder<SaveBoundaryContext>(new DbContextOptions<SaveBoundaryContext>(extensions))
            .ConfigureTestWarnings()
            .AddInterceptors(interceptors);
    }
}

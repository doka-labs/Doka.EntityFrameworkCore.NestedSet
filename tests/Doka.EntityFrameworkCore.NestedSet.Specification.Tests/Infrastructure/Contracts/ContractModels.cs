namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Creates metadata-only model variants for configuration diagnostics.</summary>
internal static class ContractModels
{
    /// <summary>Creates a context whose model cache distinguishes the requested mapping variation.</summary>
    /// <param name="variant">The model variation required by the test.</param>
    /// <returns>A context owning an isolated in-memory SQLite connection.</returns>
    internal static InvalidContext CreateVariantContext(
        string variant
    )
    {
        var optionsBuilder = new DbContextOptionsBuilder<InvalidContext>()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:")
            .UseNestedSets()
            .ReplaceService<IModelCacheKeyFactory, TestModelCacheKeyFactory>();

        return new InvalidContext(optionsBuilder.Options, variant);
    }
}

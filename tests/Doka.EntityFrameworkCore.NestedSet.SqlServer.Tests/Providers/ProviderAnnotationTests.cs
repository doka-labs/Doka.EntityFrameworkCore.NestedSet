namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Runs the shared ProviderAnnotationTests contract on this project's database provider.</summary>
public sealed class ProviderAnnotationTests : Specifications.ProviderAnnotationTests,
    IClassFixture<ProviderFixture<ProviderResources, SqlServerEngine>>
{
    /// <summary>Uses the immutable engine fixture owned by this concrete suite.</summary>
    /// <param name="fixture">The provider engine owner.</param>
    public ProviderAnnotationTests(
        ProviderFixture<ProviderResources, SqlServerEngine> fixture
    ) : base(fixture) { }
}

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Runs the shared ProviderAnnotationTests contract on this project's database provider.</summary>
public sealed class ProviderAnnotationTests : Specifications.ProviderAnnotationTests,
    IClassFixture<ProviderFixture<ProviderResources, MySqlEngine>>
{
    /// <summary>Uses the immutable engine fixture owned by this concrete suite.</summary>
    /// <param name="fixture">The provider engine owner.</param>
    public ProviderAnnotationTests(
        ProviderFixture<ProviderResources, MySqlEngine> fixture
    ) : base(fixture) { }
}

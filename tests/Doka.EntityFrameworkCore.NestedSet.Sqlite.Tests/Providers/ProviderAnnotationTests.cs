namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Runs the shared ProviderAnnotationTests contract on this project's database provider.</summary>
public sealed class ProviderAnnotationTests : Specifications.ProviderAnnotationTests,
    IClassFixture<ProviderFixture<ProviderResources, SqliteEngine>>
{
    /// <summary>Uses the immutable engine fixture owned by this concrete suite.</summary>
    /// <param name="fixture">The provider engine owner.</param>
    public ProviderAnnotationTests(
        ProviderFixture<ProviderResources, SqliteEngine> fixture
    ) : base(fixture) { }
}

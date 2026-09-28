namespace Doka.EntityFrameworkCore.NestedSet.Tests.MariaDb;

/// <summary>Runs the shared DatabaseLifecycleTests contract on this project's database provider.</summary>
public sealed class DatabaseLifecycleTests : Specifications.DatabaseLifecycleTests,
    IClassFixture<ProviderFixture<ProviderResources, MariaDbEngine>>
{
    /// <summary>Uses the immutable engine fixture owned by this concrete suite.</summary>
    /// <param name="fixture">The provider engine owner.</param>
    public DatabaseLifecycleTests(
        ProviderFixture<ProviderResources, MariaDbEngine> fixture
    ) : base(fixture) { }
}

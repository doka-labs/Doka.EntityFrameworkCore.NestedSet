namespace Doka.EntityFrameworkCore.NestedSet.Tests.MariaDb;

/// <summary>Runs the exact database-pool lifecycle regressions on MariaDb.</summary>
public sealed class DatabasePoolLifecycleTests : MySql.DatabasePoolLifecycleTestBase,
    IClassFixture<ProviderFixture<ProviderResources, MariaDbEngine>>
{
    /// <summary>Uses the neutral engine owner supplied by this provider's class fixture.</summary>
    /// <param name="fixture">The exact MariaDb resource owner.</param>
    public DatabasePoolLifecycleTests(
        ProviderFixture<ProviderResources, MariaDbEngine> fixture
    ) : base(fixture) { }
}

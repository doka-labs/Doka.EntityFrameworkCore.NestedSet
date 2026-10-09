namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Runs the exact database-pool lifecycle regressions on MySql.</summary>
public sealed class DatabasePoolLifecycleTests : DatabasePoolLifecycleTestBase,
    IClassFixture<ProviderFixture<ProviderResources, MySqlEngine>>
{
    /// <summary>Uses the neutral engine owner supplied by this provider's class fixture.</summary>
    /// <param name="fixture">The exact MySql resource owner.</param>
    public DatabasePoolLifecycleTests(
        ProviderFixture<ProviderResources, MySqlEngine> fixture
    ) : base(fixture) { }
}

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Runs the shared BroadNodeKeyTests contract on this project's database provider.</summary>
public sealed class BroadNodeKeyTests : Specifications.BroadNodeKeyTests
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public BroadNodeKeyTests(
        ProviderFixture<ModelCompatibilityDatabase, MySqlEngine> fixture
    ) : base(fixture) { }
}

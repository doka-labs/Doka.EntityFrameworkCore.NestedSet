namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Runs the shared BroadTreeIdTests contract on this project's database provider.</summary>
public sealed class BroadTreeIdTests : Specifications.BroadTreeIdTests
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public BroadTreeIdTests(
        ProviderFixture<ModelCompatibilityDatabase, SqlServerEngine> fixture
    ) : base(fixture) { }
}

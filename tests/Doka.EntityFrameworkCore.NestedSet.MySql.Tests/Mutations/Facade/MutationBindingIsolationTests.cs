namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Runs the shared MutationBindingIsolationTests contract on this project's database provider.</summary>
public sealed class MutationBindingIsolationTests : Specifications.MutationBindingIsolationTests,
    IClassFixture<ProviderFixture<ModelCompatibilityDatabase, MySqlEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public MutationBindingIsolationTests(
        ProviderFixture<ModelCompatibilityDatabase, MySqlEngine> fixture
    ) : base(fixture) { }
}

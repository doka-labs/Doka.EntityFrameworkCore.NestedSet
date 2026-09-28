namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Runs the shared ManagedSaveCallbackTests contract on this project's database provider.</summary>
public sealed class ManagedSaveCallbackTests : Specifications.ManagedSaveCallbackTests,
    IClassFixture<ProviderFixture<RelationalFixture, MySqlEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public ManagedSaveCallbackTests(
        ProviderFixture<RelationalFixture, MySqlEngine> fixture
    ) : base(fixture) { }
}

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Runs the shared SharedTypeManagedSaveTests contract on this project's database provider.</summary>
public sealed class SharedTypeManagedSaveTests : Specifications.SharedTypeManagedSaveTests
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public SharedTypeManagedSaveTests(
        ProviderFixture<ModelCompatibilityDatabase, MySqlEngine> fixture
    ) : base(fixture) { }
}

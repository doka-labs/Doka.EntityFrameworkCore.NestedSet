namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

/// <summary>Runs the shared RequiredNullableKeyModelTests contract on this project's database provider.</summary>
public sealed class RequiredNullableKeyModelTests : Specifications.RequiredNullableKeyModelTests,
    IClassFixture<ProviderFixture<ModelCompatibilityDatabase, PostgreSqlEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public RequiredNullableKeyModelTests(
        ProviderFixture<ModelCompatibilityDatabase, PostgreSqlEngine> fixture
    ) : base(fixture) { }
}

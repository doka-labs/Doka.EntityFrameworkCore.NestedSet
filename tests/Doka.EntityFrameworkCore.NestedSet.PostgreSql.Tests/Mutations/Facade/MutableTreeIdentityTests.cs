namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

/// <summary>Runs the shared MutableTreeIdentityTests contract on this project's database provider.</summary>
public sealed class MutableTreeIdentityTests : Specifications.MutableTreeIdentityTests,
    IClassFixture<ProviderFixture<Specifications.MutableTreeIdentityTests.BinaryIdentityFixture, PostgreSqlEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public MutableTreeIdentityTests(
        ProviderFixture<BinaryIdentityFixture, PostgreSqlEngine> fixture
    ) : base(fixture) { }
}

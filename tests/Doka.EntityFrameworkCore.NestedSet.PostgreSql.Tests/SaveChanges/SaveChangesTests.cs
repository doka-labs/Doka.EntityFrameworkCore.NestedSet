namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

/// <summary>Runs the shared SaveChangesTests contract on this project's database provider.</summary>
public sealed class SaveChangesTests : Specifications.SaveChangesTests,
    IClassFixture<ProviderFixture<OrderingFixture, PostgreSqlEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public SaveChangesTests(
        ProviderFixture<OrderingFixture, PostgreSqlEngine> fixture
    ) : base(fixture) { }
}

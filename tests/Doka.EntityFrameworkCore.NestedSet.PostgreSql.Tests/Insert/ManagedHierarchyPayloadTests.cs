namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

/// <summary>Runs the shared ManagedHierarchyPayloadTests contract on this project's database provider.</summary>
public sealed class ManagedHierarchyPayloadTests : Specifications.ManagedHierarchyPayloadTests,
    IClassFixture<ProviderFixture<OrderingFixture, PostgreSqlEngine>>,
    IClassFixture<ProviderFixture<RelationalFixture, PostgreSqlEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    /// <param name="orderingFixture">The fixture owned by this suite or its test collection.</param>
    public ManagedHierarchyPayloadTests(
        ProviderFixture<RelationalFixture, PostgreSqlEngine> fixture,
        ProviderFixture<OrderingFixture, PostgreSqlEngine> orderingFixture
    ) : base(fixture, orderingFixture) { }
}

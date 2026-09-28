namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Runs the shared ManagedHierarchyPayloadTests contract on this project's database provider.</summary>
public sealed class ManagedHierarchyPayloadTests : Specifications.ManagedHierarchyPayloadTests,
    IClassFixture<ProviderFixture<OrderingFixture, MySqlEngine>>,
    IClassFixture<ProviderFixture<RelationalFixture, MySqlEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    /// <param name="orderingFixture">The fixture owned by this suite or its test collection.</param>
    public ManagedHierarchyPayloadTests(
        ProviderFixture<RelationalFixture, MySqlEngine> fixture,
        ProviderFixture<OrderingFixture, MySqlEngine> orderingFixture
    ) : base(fixture, orderingFixture) { }
}

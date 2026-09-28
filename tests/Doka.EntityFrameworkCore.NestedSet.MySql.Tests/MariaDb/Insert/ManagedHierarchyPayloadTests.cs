namespace Doka.EntityFrameworkCore.NestedSet.Tests.MariaDb;

/// <summary>Runs the shared ManagedHierarchyPayloadTests contract on this project's database provider.</summary>
public sealed class ManagedHierarchyPayloadTests : Specifications.ManagedHierarchyPayloadTests,
    IClassFixture<ProviderFixture<OrderingFixture, MariaDbEngine>>,
    IClassFixture<ProviderFixture<RelationalFixture, MariaDbEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    /// <param name="orderingFixture">The fixture owned by this suite or its test collection.</param>
    public ManagedHierarchyPayloadTests(
        ProviderFixture<RelationalFixture, MariaDbEngine> fixture,
        ProviderFixture<OrderingFixture, MariaDbEngine> orderingFixture
    ) : base(fixture, orderingFixture) { }
}

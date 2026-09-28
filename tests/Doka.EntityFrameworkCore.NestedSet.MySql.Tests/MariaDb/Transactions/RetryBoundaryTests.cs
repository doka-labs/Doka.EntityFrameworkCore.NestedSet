namespace Doka.EntityFrameworkCore.NestedSet.Tests.MariaDb;

/// <summary>Runs the shared RetryBoundaryTests contract on this project's database provider.</summary>
public sealed class RetryBoundaryTests : Specifications.RetryBoundaryTests,
    IClassFixture<ProviderFixture<OrderingFixture, MariaDbEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public RetryBoundaryTests(
        ProviderFixture<OrderingFixture, MariaDbEngine> fixture
    ) : base(fixture) { }
}

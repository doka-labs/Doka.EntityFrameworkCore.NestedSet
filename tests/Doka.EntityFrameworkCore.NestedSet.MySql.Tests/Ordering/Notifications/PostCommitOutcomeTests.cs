namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Runs the shared PostCommitOutcomeTests contract on this project's database provider.</summary>
public sealed class PostCommitOutcomeTests : Specifications.PostCommitOutcomeTests,
    IClassFixture<ProviderFixture<OrderingFixture, MySqlEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public PostCommitOutcomeTests(
        ProviderFixture<OrderingFixture, MySqlEngine> fixture
    ) : base(fixture) { }
}

namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

/// <summary>Runs the shared real UserGroups sample contracts on PostgreSql.</summary>
public sealed class GroupSupervisorTests : Specifications.GroupSupervisorTests
{
    /// <summary>Uses the provider's collection-owned model database.</summary>
    /// <param name="fixture">The provider fixture.</param>
    public GroupSupervisorTests(
        ProviderFixture<ModelCompatibilityDatabase, PostgreSqlEngine> fixture
    ) : base(fixture) { }
}

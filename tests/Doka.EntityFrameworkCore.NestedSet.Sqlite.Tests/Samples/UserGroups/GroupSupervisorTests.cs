namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Runs the shared real UserGroups sample contracts on Sqlite.</summary>
public sealed class GroupSupervisorTests : Specifications.GroupSupervisorTests
{
    /// <summary>Uses the provider's collection-owned model database.</summary>
    /// <param name="fixture">The provider fixture.</param>
    public GroupSupervisorTests(ProviderFixture<ModelCompatibilityDatabase, SqliteEngine> fixture) : base(fixture)
    {
    }
}

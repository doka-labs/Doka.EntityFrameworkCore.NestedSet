namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Runs the Doka family's database-equal Scope rowset regressions on MySQL.</summary>
public sealed class ScopeAliasRowsetTests : ScopeAliasRowsetTestBase,
    IClassFixture<ProviderFixture<RelationalFixture, MySqlEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixture.</summary>
    /// <param name="fixture">The exact MySql database owner.</param>
    public ScopeAliasRowsetTests(
        ProviderFixture<RelationalFixture, MySqlEngine> fixture
    ) : base(fixture) { }
}

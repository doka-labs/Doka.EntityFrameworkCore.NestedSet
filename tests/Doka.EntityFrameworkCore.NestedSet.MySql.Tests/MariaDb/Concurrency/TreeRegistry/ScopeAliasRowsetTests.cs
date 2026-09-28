namespace Doka.EntityFrameworkCore.NestedSet.Tests.MariaDb;

/// <summary>Runs the Doka family's database-equal Scope rowset regressions on MariaDB.</summary>
public sealed class ScopeAliasRowsetTests : MySql.ScopeAliasRowsetTestBase,
    IClassFixture<ProviderFixture<RelationalFixture, MariaDbEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixture.</summary>
    /// <param name="fixture">The exact MariaDb database owner.</param>
    public ScopeAliasRowsetTests(
        ProviderFixture<RelationalFixture, MariaDbEngine> fixture
    ) : base(fixture) { }
}

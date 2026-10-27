namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Executes the dense path, fringe and isolated forest native-work contracts on MySQL.</summary>
public sealed class DensePathWorkTests : DensePathWorkTestBase,
    IClassFixture<ProviderFixture<RelationalFixture, MySqlEngine>>
{
    /// <summary>Uses the concrete MySQL fixture and its native-work output sink.</summary>
    /// <param name="fixture">The engine-bound isolated relational resource.</param>
    /// <param name="output">The sink retaining actual plan and construction observations.</param>
    public DensePathWorkTests(
        ProviderFixture<RelationalFixture, MySqlEngine> fixture,
        ITestOutputHelper output
    ) : base(fixture, output) { }
}

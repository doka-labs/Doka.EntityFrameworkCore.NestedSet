namespace Doka.EntityFrameworkCore.NestedSet.Tests.MariaDb;

/// <summary>Executes the same dense path, fringe and isolated forest contracts on MariaDB.</summary>
public sealed class DensePathWorkTests : MySql.DensePathWorkTestBase,
    IClassFixture<ProviderFixture<RelationalFixture, MariaDbEngine>>
{
    /// <summary>Uses the concrete MariaDB fixture and its native-work output sink.</summary>
    /// <param name="fixture">The engine-bound isolated relational resource.</param>
    /// <param name="output">The sink retaining actual plan and construction observations.</param>
    public DensePathWorkTests(
        ProviderFixture<RelationalFixture, MariaDbEngine> fixture,
        ITestOutputHelper output
    ) : base(fixture, output) { }
}

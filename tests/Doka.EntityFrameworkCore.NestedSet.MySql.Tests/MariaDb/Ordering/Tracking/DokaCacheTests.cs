namespace Doka.EntityFrameworkCore.NestedSet.Tests.MariaDb;

/// <summary>Runs the Doka family's reseeded refresh-cache measurements on MariaDB.</summary>
[Collection("Allocation measurements")]
public sealed class DokaCacheTests : MySql.DokaCacheTestBase,
    IClassFixture<ProviderFixture<OrderingFixture, MariaDbEngine>>
{
    /// <summary>Uses this suite's isolated ordering database and measurement output.</summary>
    /// <param name="fixture">The MariaDB ordering fixture owned by this suite.</param>
    /// <param name="output">The measurement's test-output sink.</param>
    public DokaCacheTests(
        ProviderFixture<OrderingFixture, MariaDbEngine> fixture,
        ITestOutputHelper output
    ) : base(fixture, output) { }
}

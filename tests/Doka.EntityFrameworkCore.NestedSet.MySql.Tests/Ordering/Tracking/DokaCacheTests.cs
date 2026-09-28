namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Runs the Doka family's reseeded refresh-cache measurements on MySQL.</summary>
[Collection("Allocation measurements")]
public sealed class DokaCacheTests : DokaCacheTestBase, IClassFixture<ProviderFixture<OrderingFixture, MySqlEngine>>
{
    /// <summary>Uses this suite's isolated ordering database and measurement output.</summary>
    /// <param name="fixture">The MySQL ordering fixture owned by this suite.</param>
    /// <param name="output">The measurement's test-output sink.</param>
    public DokaCacheTests(
        ProviderFixture<OrderingFixture, MySqlEngine> fixture,
        ITestOutputHelper output
    ) : base(fixture, output) { }
}

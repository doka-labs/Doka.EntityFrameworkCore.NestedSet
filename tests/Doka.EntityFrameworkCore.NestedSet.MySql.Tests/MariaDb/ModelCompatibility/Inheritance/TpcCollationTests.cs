namespace Doka.EntityFrameworkCore.NestedSet.Tests.MariaDb;

/// <summary>Runs the Doka family's concrete TPC native-identity collation regressions on MariaDB.</summary>
[Collection("Model compatibility")]
public sealed class TpcCollationTests : MySql.TpcCollationTestBase
{
    /// <summary>Uses the exact engine fixture registered for this provider suite.</summary>
    /// <param name="fixture">The isolated provider resource from the suite or its collection.</param>
    public TpcCollationTests(
        ProviderFixture<ModelCompatibilityDatabase, MariaDbEngine> fixture
    ) : base(fixture) { }
}

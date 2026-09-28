namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Runs the Doka family's concrete TPC native-identity collation regressions on MySQL.</summary>
[Collection("Model compatibility")]
public sealed class TpcCollationTests : TpcCollationTestBase
{
    /// <summary>Uses the exact engine fixture registered for this provider suite.</summary>
    /// <param name="fixture">The isolated provider resource from the suite or its collection.</param>
    public TpcCollationTests(
        ProviderFixture<ModelCompatibilityDatabase, MySqlEngine> fixture
    ) : base(fixture) { }
}

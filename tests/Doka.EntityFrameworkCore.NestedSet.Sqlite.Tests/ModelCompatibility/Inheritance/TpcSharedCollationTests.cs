namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Runs the shared TpcSharedCollationTests contract on this project's database provider.</summary>
public sealed class TpcSharedCollationTests : Specifications.TpcSharedCollationTests
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public TpcSharedCollationTests(
        ProviderFixture<ModelCompatibilityDatabase, SqliteEngine> fixture
    ) : base(fixture) { }
}

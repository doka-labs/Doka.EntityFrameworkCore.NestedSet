namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Runs the shared CompositeKeyTests contract on this project's database provider.</summary>
public sealed class CompositeKeyTests : Specifications.CompositeKeyTests
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public CompositeKeyTests(
        ProviderFixture<ModelCompatibilityDatabase, SqliteEngine> fixture
    ) : base(fixture) { }
}

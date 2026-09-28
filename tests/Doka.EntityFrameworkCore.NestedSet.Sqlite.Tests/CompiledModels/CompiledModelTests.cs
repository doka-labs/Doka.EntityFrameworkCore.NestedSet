namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Runs the shared CompiledModelTests contract on this project's database provider.</summary>
public sealed class CompiledModelTests : Specifications.CompiledModelTests,
    IClassFixture<ProviderFixture<CompiledModelFixture, SqliteEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public CompiledModelTests(
        ProviderFixture<CompiledModelFixture, SqliteEngine> fixture
    ) : base(fixture) { }
}

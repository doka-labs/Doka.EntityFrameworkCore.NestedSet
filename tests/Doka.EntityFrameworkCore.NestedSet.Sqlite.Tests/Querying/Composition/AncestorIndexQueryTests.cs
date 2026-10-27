namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Runs indexed ancestor query contracts on SQLite.</summary>
public sealed class AncestorIndexQueryTests : Specifications.AncestorIndexQueryTests
{
    /// <summary>Uses the collection-owned SQLite model database.</summary>
    /// <param name="fixture">The exact SQLite provider fixture.</param>
    public AncestorIndexQueryTests(ProviderFixture<ModelCompatibilityDatabase, SqliteEngine> fixture) : base(fixture) { }
}

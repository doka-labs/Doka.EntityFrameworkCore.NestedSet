namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Runs the shared CompositeKeyTests contract on this project's database provider.</summary>
// WHY: xUnit analyzer 2.3.0 misses inherited collections from referenced assemblies (xunit/xunit#3666).
[Collection("Model compatibility")]
public sealed class CompositeKeyTests : Specifications.CompositeKeyTests
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public CompositeKeyTests(
        ProviderFixture<ModelCompatibilityDatabase, SqlServerEngine> fixture
    ) : base(fixture) { }
}

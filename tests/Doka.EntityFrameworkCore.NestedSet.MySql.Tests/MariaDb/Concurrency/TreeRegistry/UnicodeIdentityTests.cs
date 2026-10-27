namespace Doka.EntityFrameworkCore.NestedSet.Tests.MariaDb;

/// <summary>Runs the Doka family's Unicode registry identity contracts on MariaDB.</summary>
[Collection("Model compatibility")]
public sealed class UnicodeIdentityTests : MySql.UnicodeIdentityTestBase
{
    /// <summary>Uses the provider collection's existing database and native evidence output.</summary>
    /// <param name="fixture">The exact MariaDB compatibility database owner.</param>
    /// <param name="output">The output retaining native character and octet lengths.</param>
    public UnicodeIdentityTests(
        ProviderFixture<ModelCompatibilityDatabase, MariaDbEngine> fixture,
        ITestOutputHelper output
    ) : base(fixture, output) { }
}

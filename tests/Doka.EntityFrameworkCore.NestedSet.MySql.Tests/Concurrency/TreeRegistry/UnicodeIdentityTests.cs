namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Runs the Doka family's Unicode registry identity contracts on MySQL.</summary>
[Collection("Model compatibility")]
public sealed class UnicodeIdentityTests : UnicodeIdentityTestBase
{
    /// <summary>Uses the provider collection's existing database and native evidence output.</summary>
    /// <param name="fixture">The exact MySQL compatibility database owner.</param>
    /// <param name="output">The output retaining native character and octet lengths.</param>
    public UnicodeIdentityTests(
        ProviderFixture<ModelCompatibilityDatabase, MySqlEngine> fixture,
        ITestOutputHelper output
    ) : base(fixture, output) { }
}

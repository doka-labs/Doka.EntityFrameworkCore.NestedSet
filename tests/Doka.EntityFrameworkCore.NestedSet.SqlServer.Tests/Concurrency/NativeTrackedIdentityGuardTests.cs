namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Runs the shared NativeTrackedIdentityGuardTests contract on this project's database provider.</summary>
public sealed class NativeTrackedIdentityGuardTests : Specifications.NativeTrackedIdentityGuardTests
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public NativeTrackedIdentityGuardTests(
        ProviderFixture<ModelCompatibilityDatabase, SqlServerEngine> fixture
    ) : base(fixture) { }

    /// <summary>Runs the shared Scale contract on this project's database provider.</summary>
    public new sealed class Scale : Specifications.NativeTrackedIdentityGuardTests.Scale,
        IClassFixture<ProviderFixture<ModelCompatibilityDatabase, SqlServerEngine>>
    {
        /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
        /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
        /// <param name="output">The test output sink.</param>
        public Scale(
            ProviderFixture<ModelCompatibilityDatabase, SqlServerEngine> fixture,
            ITestOutputHelper output
        ) : base(fixture, output) { }
    }
}

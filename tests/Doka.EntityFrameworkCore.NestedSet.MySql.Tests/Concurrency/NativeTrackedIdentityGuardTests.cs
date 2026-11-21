namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Runs the shared NativeTrackedIdentityGuardTests contract on this project's database provider.</summary>
public sealed class NativeTrackedIdentityGuardTests : Specifications.NativeTrackedIdentityGuardTests
{
    /// <summary>Uses the provider assembly's isolated fixture.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public NativeTrackedIdentityGuardTests(
        ProviderFixture<ModelCompatibilityDatabase, MySqlEngine> fixture
    ) : base(fixture) { }

    /// <summary>Runs the shared Scale contract on this project's database provider.</summary>
    public new sealed class Scale : Specifications.NativeTrackedIdentityGuardTests.Scale,
        IClassFixture<ProviderFixture<ModelCompatibilityDatabase, MySqlEngine>>
    {
        /// <summary>Uses the provider assembly's isolated fixture.</summary>
        /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
        public Scale(
            ProviderFixture<ModelCompatibilityDatabase, MySqlEngine> fixture
        ) : base(fixture) { }
    }
}

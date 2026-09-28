namespace Doka.EntityFrameworkCore.NestedSet.Tests.MariaDb;

/// <summary>Runs the shared MappingDeletionTests contract on this project's database provider.</summary>
public sealed class MappingDeletionTests : Specifications.MappingDeletionTests
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public MappingDeletionTests(
        ProviderFixture<ModelCompatibilityDatabase, MariaDbEngine> fixture
    ) : base(fixture) { }
}

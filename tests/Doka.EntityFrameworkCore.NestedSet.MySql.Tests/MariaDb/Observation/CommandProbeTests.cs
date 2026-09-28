namespace Doka.EntityFrameworkCore.NestedSet.Tests.MariaDb;

/// <summary>Runs the shared CommandProbeTests contract on this project's database provider.</summary>
public sealed class CommandProbeTests : Specifications.CommandProbeTests,
    IClassFixture<ProviderFixture<RelationalFixture, MariaDbEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public CommandProbeTests(
        ProviderFixture<RelationalFixture, MariaDbEngine> fixture
    ) : base(fixture) { }
}

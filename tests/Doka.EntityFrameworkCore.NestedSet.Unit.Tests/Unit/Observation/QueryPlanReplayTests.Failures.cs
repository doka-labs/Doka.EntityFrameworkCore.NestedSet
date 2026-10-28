namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class QueryPlanReplayTests
{
    /// <summary>Failure evidence selects the actual query after setup and preserves diagnostic failure separately.</summary>
    /// <param name="diagnosticFails">Whether estimated-plan execution itself fails.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureEvidenceRetainsOriginalQueryAfterFixtureCommands(
        bool diagnosticFails
    )
    {
        // Arrange
        await using var connection = new PlanReplayConnection(() => diagnosticFails
            ? throw new InvalidOperationException("Estimated plan unavailable.")
            : Results(Plan("estimated target plan")));

        await using var context = CreateContext(connection);
        var probe = Probe();
        probe.Commands.Add("SELECT 2");
        probe.ParameterBindings.Add([]);
        var original = new TimeoutException("Original target read timed out.");
        var profile = "unit-" + Guid.NewGuid().ToString("N");
        var directory = Environment.GetEnvironmentVariable("DOKA_NESTEDSET_QUERY_PLAN_DIR")
            ?? Path.Combine(Path.GetTempPath(), "doka-nestedset-query-plans");

        var path = Path.Combine(directory, "PostgreSql-composition-capacity-failed-" + profile + ".txt");

        try
        {
            // Act
            await CompositionCapacityProfiles.RetainFailureAsync(context, "PostgreSql", profile, probe, original, 1);
            var evidence = await File.ReadAllTextAsync(path, CancellationToken.None);

            // Assert
            Assert.NotNull(connection.Command);
            Assert.Equal("EXPLAIN (FORMAT JSON) SELECT 2", connection.Command.CommandText);
            Assert.Contains(original.ToString(), evidence, StringComparison.Ordinal);
            Assert.Contains("SELECT 2", evidence, StringComparison.Ordinal);
            Assert.DoesNotContain("SELECT 1", evidence, StringComparison.Ordinal);
            Assert.Contains(diagnosticFails ? "Estimated-plan capture also failed:" : "estimated target plan",
                evidence, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

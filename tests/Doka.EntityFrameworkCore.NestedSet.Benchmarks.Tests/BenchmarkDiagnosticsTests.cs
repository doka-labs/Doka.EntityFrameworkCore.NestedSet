namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>Verifies that untimed command instrumentation observes only explicitly enabled operations.</summary>
public sealed class BenchmarkDiagnosticsTests
{
    /// <summary>
    /// Preparation leaves zero counters, and measurement instrumentation is absent from timing fixtures.
    /// </summary>
    /// <param name="diagnostics">Whether this is the separate untimed diagnostic fixture.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreparedCounterObservesOnlyEnabledOperation(
        bool diagnostics
    )
    {
        // Arrange
        await using var fixture = BenchmarkFixture.Create(
            new BenchmarkScenario(20, BenchmarkShape.Wide, 1, 0),
            false,
            new BenchmarkRunOptions { Diagnostics = diagnostics },
            "Data Source=:memory:;Pooling=False",
            CancellationToken.None);

        await fixture.ResetAsync(true, CancellationToken.None);
        var commandsBefore = fixture.Counter.Commands;
        var updatesBefore = fixture.Counter.Updates;
        var rowsBefore = fixture.Counter.UpdatedRows;

        // Act
        await fixture.Hierarchy.DeleteSubtreeAsync(fixture.LeafKey, CancellationToken.None);

        // Assert
        Assert.Equal(0, commandsBefore);
        Assert.Equal(0, updatesBefore);
        Assert.Equal(0L, rowsBefore);
        Assert.Equal(diagnostics, fixture.Counter.Commands > 0);
        Assert.Equal(diagnostics, fixture.Counter.Updates > 0);
        Assert.Equal(diagnostics, fixture.Counter.UpdatedRows > 0);
        await fixture.ValidateAsync(CancellationToken.None);
        Assert.Equal(19, await fixture.Context.Nodes.CountAsync(CancellationToken.None));
    }
}

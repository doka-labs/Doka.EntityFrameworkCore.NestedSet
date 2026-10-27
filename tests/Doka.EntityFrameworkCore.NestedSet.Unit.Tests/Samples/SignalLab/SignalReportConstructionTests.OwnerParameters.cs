using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.SignalLab;

public sealed partial class SignalReportConstructionTests
{
    /// <summary>Owner scalar replacement preserves the full report's warmed SQL shape and private bindings.</summary>
    /// <param name="engine">The actual provider dialect configured without server discovery.</param>
    /// <param name="nearest">Whether classification selection precedes checking the nearest owner's values.</param>
    [Theory]
    [InlineData("MySql", false)]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", false)]
    [InlineData("MariaDb", true)]
    [InlineData("PostgreSql", false)]
    [InlineData("PostgreSql", true)]
    [InlineData("SqlServer", false)]
    [InlineData("SqlServer", true)]
    [InlineData("Sqlite", false)]
    [InlineData("Sqlite", true)]
    public async Task OwnerPredicatesKeepWarmShapeForEqualAndDistinctScalars(string engine, bool nearest)
    {
        // Arrange
        var compilations = new List<string>();
        var boundary = new CompositionIoBoundary();
        var options = ContextOptions(engine, boundary)
            .LogTo(compilations.Add, [CoreEventId.QueryCompilationStarting], LogLevel.Debug);

        await using var provider = CreateServiceProvider(options.Options);
        await using var context = new SignalLabContext(options.UseInternalServiceProvider(provider).Options);
        var (first, second) = ParameterRequests(nearest);

        // WHY: This warms the actual hierarchy, owner, temporal path and aggregate shape, rather than a
        // standalone equality. Changing one scalar must not merge parameter slots or recompile that shape.
        _ = SignalReportBuilder.Build(context, first).ToQueryString();
        compilations.Clear();

        // Act
        var secondSql = SignalReportBuilder.Build(context, second).ToQueryString();
        var firstSql = SignalReportBuilder.Build(context, first).ToQueryString();

        // Assert
        Assert.Contains("Shared", firstSql, StringComparison.Ordinal);
        Assert.Contains("Distinct", secondSql, StringComparison.Ordinal);
        Assert.DoesNotContain("Distinct", firstSql, StringComparison.Ordinal);
        Assert.Empty(compilations);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}

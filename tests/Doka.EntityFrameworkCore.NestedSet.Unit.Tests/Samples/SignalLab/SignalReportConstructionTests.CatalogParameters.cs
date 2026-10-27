using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.SignalLab;

public sealed partial class SignalReportConstructionTests
{
    /// <summary>Only context-free catalog projections are reused between independently bound constructions.</summary>
    /// <param name="engine">The actual provider dialect configured without server discovery.</param>
    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    [InlineData("Sqlite")]
    public async Task CatalogReusesFixedProjectionsWithoutReusingRequestBindings(string engine)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = ContextOptions(engine, boundary);
        await using var provider = CreateServiceProvider(options.Options);
        await using var context = new SignalLabContext(options.UseInternalServiceProvider(provider).Options);
        var firstRequest = Request(false);
        var secondRequest = Request(true);

        // Act
        var firstQuery = SignalReportBuilder.Catalog(context, firstRequest);
        var secondQuery = SignalReportBuilder.Catalog(context, secondRequest);
        var first = CatalogProjectionCollector.Read(firstQuery.Expression);
        var second = CatalogProjectionCollector.Read(secondQuery.Expression);

        // Assert
        Assert.NotSame(firstQuery, secondQuery);
        Assert.Equal(4, first.Count);
        Assert.Equal(first.Count, second.Count);
        Assert.All(first.Zip(second), pair => Assert.Same(pair.First, pair.Second));
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Scalar equality does not merge distinct request slots or invalidate a warmed catalog shape.</summary>
    /// <param name="engine">The actual provider dialect configured without server discovery.</param>
    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    [InlineData("Sqlite")]
    public async Task CatalogWarmShapeSurvivesEqualAndDistinctScalarValues(string engine)
    {
        // Arrange
        var compilations = new List<string>();
        var boundary = new CompositionIoBoundary();
        var options = ContextOptions(engine, boundary)
            .LogTo(compilations.Add, [CoreEventId.QueryCompilationStarting], LogLevel.Debug);

        await using var provider = CreateServiceProvider(options.Options);
        await using var context = new SignalLabContext(options.UseInternalServiceProvider(provider).Options);
        var (first, second) = ParameterRequests(false);

        // WHY: Warm an equal-value request before changing only scalar values. Constant-value deduplication
        // must not turn those values into a different parameter graph and trigger another SQL compilation.
        _ = SignalReportBuilder.Catalog(context, first).ToQueryString();
        compilations.Clear();

        // Act
        var secondSql = SignalReportBuilder.Catalog(context, second).ToQueryString();
        var firstSql = SignalReportBuilder.Catalog(context, first).ToQueryString();

        // Assert
        Assert.Contains("Shared", firstSql, StringComparison.Ordinal);
        Assert.Contains("Distinct", secondSql, StringComparison.Ordinal);
        Assert.DoesNotContain("Distinct", firstSql, StringComparison.Ordinal);
        Assert.Empty(compilations);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Creates equal and distinct scalar requests with identical field, mode and hierarchy shapes.</summary>
    private static (ValidatedSignalReport First, ValidatedSignalReport Second) ParameterRequests(bool nearest)
    {
        var mode = nearest ? TagOwnerMode.Nearest : TagOwnerMode.Any;
        var first = Request(nearest) with
        {
            Groups =
            [
                new TagGroup(SignalReportDataset.SensorKind, mode,
                [
                    new TagCriterion(SignalReportDataset.SensorKind, "Cooling", "Shared", mode),
                    new TagCriterion(SignalReportDataset.SensorKind, "RackCode", "Shared", mode),
                ]),
            ],
        };

        var second = first with
        {
            Groups =
            [
                first.Groups[0] with
                {
                    Criteria =
                    [
                        first.Groups[0].Criteria[0],
                        first.Groups[0].Criteria[1] with { Value = "Distinct" },
                    ],
                },
            ],
        };

        return (first, second);
    }

    /// <summary>Finds the fixed DTO projections without evaluating captured request or context expressions.</summary>
    private sealed class CatalogProjectionCollector : ExpressionVisitor
    {
        private readonly List<LambdaExpression> _projections = [];

        /// <summary>Reads the four catalog branches in ordinary expression traversal order.</summary>
        internal static List<LambdaExpression> Read(Expression expression)
        {
            var collector = new CatalogProjectionCollector();
            collector.Visit(expression);

            return collector._projections;
        }

        /// <inheritdoc />
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(Queryable)
                && node.Method.Name == nameof(Queryable.Select)
                && node.Arguments[1] is UnaryExpression { Operand: LambdaExpression projection }
                && projection.ReturnType == typeof(SignalCatalogRow))
            {
                _projections.Add(projection);
            }

            return base.VisitMethodCall(node);
        }
    }
}

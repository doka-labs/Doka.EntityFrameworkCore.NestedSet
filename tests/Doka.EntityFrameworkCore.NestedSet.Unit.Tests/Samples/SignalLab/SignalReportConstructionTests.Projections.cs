using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.SignalLab;

public sealed partial class SignalReportConstructionTests
{
    /// <summary>Defines the complete scalar proof grain in stable ordinal member order.</summary>
    private static readonly string[] s_qualifiedPathGrain = ["AnchorId", "Day", "NodeId", "NodeTypeId", "TenantId"];

    /// <summary>Names the immutable graphs retained directly by each deferred report expression.</summary>
    private static readonly string[] s_retainedReportProjections =
    [
        "MeasurementDay", "Path", "QualifiedPath", "Measurement", "Definition", "MatchingMeasurementType",
    ];

    /// <summary>Reusable report graphs cannot retain a context, query root or mutable request binding.</summary>
    [Fact]
    public void FixedReportProjectionsContainOnlyTheirParametersAndScalarConstants()
    {
        // Arrange
        var projections = ReportProjectionFields();
        var inspector = new ProjectionConstantInspector();

        // Act
        foreach (var projection in projections.Values)
        {
            inspector.Visit(projection);
        }

        // Assert
        Assert.Equal(9, projections.Count);
        Assert.Empty(inspector.CapturedObjects);
    }

    /// <summary>The ownership inspector rejects captures instead of merely accepting every expression graph.</summary>
    [Fact]
    public void FixedProjectionCaptureInspectorRejectsRequestAndQueryRoots()
    {
        // Arrange
        var request = Request(false);
        var query = Array.Empty<SignalNode>().AsQueryable();
        var capturedRequest = Expression.Constant(request);
        var capturedRoot = Expression.Constant(query);
        var inspector = new ProjectionConstantInspector();

        // Act
        inspector.Visit(capturedRequest);
        inspector.Visit(capturedRoot);

        // Assert
        Assert.Equal(2, inspector.CapturedObjects.Count);
        Assert.Same(request, inspector.CapturedObjects[0]);
        Assert.Same(query, inspector.CapturedObjects[1]);
    }

    /// <summary>
    /// Independent request constructions share fixed graphs without sharing query or scalar bindings.
    /// </summary>
    /// <param name="engine">The actual provider dialect configured without server discovery.</param>
    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    [InlineData("Sqlite")]
    public async Task ReportsReuseFixedGraphsAcrossIndependentRequests(string engine)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = ContextOptions(engine, boundary);
        await using var provider = CreateServiceProvider(options.Options);
        await using var context = new SignalLabContext(options.UseInternalServiceProvider(provider).Options);
        var firstRequest = Request(false);
        var secondRequest = Request(true);
        var fixedGraphs = ReportProjectionFields();

        // Act
        var first = SignalReportBuilder.Build(context, firstRequest);
        var second = SignalReportBuilder.Build(context, secondRequest);
        var firstGraphs = ReportGraphInspector.Read(first.Expression);
        var secondGraphs = ReportGraphInspector.Read(second.Expression);

        // Assert
        Assert.NotSame(first, second);
        Assert.NotEqual(firstRequest.TenantId, secondRequest.TenantId);
        Assert.NotEqual(firstRequest.TreeId, secondRequest.TreeId);
        foreach (var name in s_retainedReportProjections)
        {
            Assert.Contains(firstGraphs.Lambdas, lambda => ReferenceEquals(lambda, fixedGraphs[name]));
            Assert.Contains(secondGraphs.Lambdas, lambda => ReferenceEquals(lambda, fixedGraphs[name]));
        }

        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>The unique proof grain remains scalar and precedes restoration of independent physical facts.</summary>
    /// <param name="nearest">Whether metadata rejects nonmatching nearest owners without falling back.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReportRetainsScalarProofBoundaryBeforeRestoringTheFactBag(bool nearest)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = ContextOptions("Sqlite", boundary);
        await using var provider = CreateServiceProvider(options.Options);
        await using var context = new SignalLabContext(options.UseInternalServiceProvider(provider).Options);
        var request = Request(nearest);
        var fixedGraphs = ReportProjectionFields();

        // Act
        var report = SignalReportBuilder.Build(context, request);
        var graph = ReportGraphInspector.Read(report.Expression);

        // Assert
        var projection = fixedGraphs["QualifiedPath"];
        var properties = projection.ReturnType.GetProperties();
        Assert.Equal(s_qualifiedPathGrain,
            properties.Select(property => property.Name).OrderBy(name => name));
        Assert.All(properties, property => Assert.True(property.PropertyType.IsValueType));
        var measurementJoin = Assert.Single(graph.Calls, call => call.Method.DeclaringType == typeof(Queryable)
            && call.Method.Name == nameof(Queryable.Join)
            && call.Arguments[^1] is UnaryExpression { Operand: LambdaExpression result }
            && ReferenceEquals(result, fixedGraphs["Measurement"]));

        var distinct = Assert.IsAssignableFrom<MethodCallExpression>(measurementJoin.Arguments[0]);
        Assert.Equal(nameof(Queryable.Distinct), distinct.Method.Name);
        var select = Assert.IsAssignableFrom<MethodCallExpression>(distinct.Arguments[0]);
        Assert.Equal(nameof(Queryable.Select), select.Method.Name);
        var selected = Assert.IsAssignableFrom<UnaryExpression>(select.Arguments[1]);
        Assert.Same(projection, selected.Operand);
        Assert.DoesNotContain(graph.Calls, call => call.Method.DeclaringType == typeof(Queryable)
            && call.Method.Name == nameof(Queryable.Distinct)
            && call.Method.GetGenericArguments()[0].Name is "MeasurementRow" or "MeasuredContribution"
                or "DefinedContribution");

        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Reads private templates without exposing test-only APIs in the independently usable sample.</summary>
    private static Dictionary<string, LambdaExpression> ReportProjectionFields()
    {
        var type = typeof(SignalReportBuilder).GetNestedType("ReportProjections", BindingFlags.NonPublic);
        Assert.NotNull(type);

        return type.GetFields(BindingFlags.Static | BindingFlags.NonPublic)
            .ToDictionary(field => field.Name,
                field => Assert.IsAssignableFrom<LambdaExpression>(field.GetValue(null)));
    }

    /// <summary>Rejects context, query and holder captures from reusable expression graphs.</summary>
    private sealed class ProjectionConstantInspector : ExpressionVisitor
    {
        /// <summary>Gets non-scalar captures that would retain a request or application state.</summary>
        internal List<object> CapturedObjects { get; } = [];

        /// <inheritdoc />
        protected override Expression VisitConstant(ConstantExpression node)
        {
            if (node.Value is not null && node.Value is not string && !node.Type.IsValueType)
            {
                CapturedObjects.Add(node.Value);
            }

            return node;
        }
    }

    /// <summary>
    /// Inspects expression identity and bag boundaries without evaluating any application expression.
    /// </summary>
    private sealed class ReportGraphInspector : ExpressionVisitor
    {
        /// <summary>Gets every referenced lambda, including the immutable shared projection instances.</summary>
        internal List<LambdaExpression> Lambdas { get; } = [];

        /// <summary>Gets deferred calls used to locate the scalar proof and subsequent physical fact join.</summary>
        internal List<MethodCallExpression> Calls { get; } = [];

        /// <summary>Visits one report's deferred graph without causing provider execution.</summary>
        internal static ReportGraphInspector Read(Expression expression)
        {
            var inspector = new ReportGraphInspector();
            inspector.Visit(expression);

            return inspector;
        }

        /// <inheritdoc />
        protected override Expression VisitLambda<T>(Expression<T> node)
        {
            Lambdas.Add(node);

            return base.VisitLambda(node);
        }

        /// <inheritdoc />
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            Calls.Add(node);

            return base.VisitMethodCall(node);
        }
    }
}

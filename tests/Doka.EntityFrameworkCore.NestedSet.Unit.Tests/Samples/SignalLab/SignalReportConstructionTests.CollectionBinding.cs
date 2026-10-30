using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Providers;
using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.SignalLab;

public sealed partial class SignalReportConstructionTests
{
    /// <summary>The shared tracked-key path binds generic key membership without a span conversion.</summary>
    /// <param name="engine">An adapter using the shared collection-membership implementation.</param>
    [Theory]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    [InlineData("Sqlite")]
    public async Task TrackedKeyCollectionsBindToEnumerableBeforeProviderNormalization(string engine)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = ContextOptions(engine, boundary);
        await using var provider = CreateServiceProvider(options.Options);
        await using var context = new SignalLabContext(options.UseInternalServiceProvider(provider).Options);
        var key = context.Model.FindEntityType(typeof(SignalNode))!.FindProperty(nameof(SignalNode.Id))!;
        Guid[] keys = [SignalReportDataset.Id(1), SignalReportDataset.Id(2)];
        var probe = new CollectionMembershipProbe();

        // Act
        var query = NestedSetProviderCapabilities.Resolve(context).MatchTrackedKeyCollection(context.Nodes, key, keys);
        probe.Visit(query.Expression);
        var sql = query.ToQueryString();

        // Assert
        Assert.Equal(typeof(Enumerable), Assert.Single(probe.Memberships).Method.DeclaringType);
        Assert.Equal(1, probe.ParameterMarkers);
        Assert.NotEmpty(sql);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Collection membership binds before EF can normalize C# 14 span overloads.</summary>
    /// <param name="engine">The configured provider dialect, without database discovery.</param>
    /// <param name="catalog">Whether to inspect catalog selection instead of report composition.</param>
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
    public async Task CollectionMembershipBindsToEnumerableBeforeProviderNormalization(string engine, bool catalog)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = ContextOptions(engine, boundary);
        await using var provider = CreateServiceProvider(options.Options);
        await using var context = new SignalLabContext(options.UseInternalServiceProvider(provider).Options);
        var request = Request(false);
        var probe = new CollectionMembershipProbe();

        // Act
        var expression = catalog
            ? SignalReportBuilder.Catalog(context, request).Expression
            : SignalReportBuilder.Build(context, request).Expression;

        probe.Visit(expression);

        // Assert
        Assert.NotEmpty(probe.Memberships);
        Assert.All(probe.Memberships, call => Assert.Equal(typeof(Enumerable), call.Method.DeclaringType));
        Assert.All(probe.Memberships, call => Assert.All(call.Arguments,
            argument => Assert.False(argument.Type.IsByRefLike)));
        Assert.Equal(engine == "Sqlite", probe.ParameterMarkers != 0);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>The actual anchor predicate supports interpretation and ordinary set-membership semantics.</summary>
    /// <param name="engine">The ordinary or explicitly parameterized catalog branch.</param>
    /// <param name="selection">The selected key arrangement, including empty and duplicate inputs.</param>
    /// <param name="expected">Whether the independently chosen candidate must match.</param>
    [Theory]
    [InlineData("MySql", "match", true)]
    [InlineData("MySql", "missing", false)]
    [InlineData("MySql", "empty", false)]
    [InlineData("MySql", "duplicates", true)]
    [InlineData("Sqlite", "match", true)]
    [InlineData("Sqlite", "missing", false)]
    [InlineData("Sqlite", "empty", false)]
    [InlineData("Sqlite", "duplicates", true)]
    public async Task ActualAnchorMembershipSupportsInterpretation(string engine, string selection, bool expected)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = ContextOptions(engine, boundary);
        await using var provider = CreateServiceProvider(options.Options);
        await using var context = new SignalLabContext(options.UseInternalServiceProvider(provider).Options);
        var candidate = new SignalNode { Id = SignalReportDataset.Id(1) };
        Guid[] keys = selection switch
        {
            "match" => [candidate.Id],
            "missing" => [SignalReportDataset.Id(2)],
            "empty" => [],
            "duplicates" => [candidate.Id, candidate.Id],
            _ => throw new ArgumentOutOfRangeException(nameof(selection)),
        };

        var request = Request(false) with { AnchorIds = keys };
        var probe = new AnchorMembershipProbe();
        probe.Visit(SignalReportBuilder.Catalog(context, request).Expression);
        var predicate = Assert.Single(probe.AnchorPredicates);

        // WHY: EF.Parameter is an SQL parameterization marker, not a CLR operation. Remove only that
        // marker so the interpreter exercises the sample's actual Contains binding without EF rewriting it.
        var interpretedPredicate = (Expression<Func<SignalNode, bool>>)new ParameterMarkerIdentity().Visit(predicate);

        // Act
        var actual = interpretedPredicate.Compile(preferInterpretation: true)(candidate);

        // Assert
        Assert.Equal(expected, actual);
        Assert.Equal(engine == "Sqlite" ? 2 : 0, probe.ParameterMarkers);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Locates the sample-specific anchor predicate within the original collection query graph.</summary>
    private sealed class AnchorMembershipProbe : CollectionMembershipProbe
    {
        /// <summary>Gets the catalog's direct anchor-membership predicates.</summary>
        internal List<Expression<Func<SignalNode, bool>>> AnchorPredicates { get; } = [];

        /// <inheritdoc />
        protected override Expression VisitLambda<T>(Expression<T> node)
        {
            if (node is Expression<Func<SignalNode, bool>> predicate
                && predicate.Body is MethodCallExpression { Method.Name: nameof(Enumerable.Contains) })
            {
                AnchorPredicates.Add(predicate);
            }

            return base.VisitLambda(node);
        }
    }

    /// <summary>Erases only the EF parameterization marker for a pure CLR predicate interpretation.</summary>
    private sealed class ParameterMarkerIdentity : ExpressionVisitor
    {
        /// <inheritdoc />
        protected override Expression VisitMethodCall(MethodCallExpression node)
            => node.Method.DeclaringType == typeof(EF) && node.Method.Name == nameof(EF.Parameter)
                ? Visit(node.Arguments[0])
                : base.VisitMethodCall(node);
    }
}

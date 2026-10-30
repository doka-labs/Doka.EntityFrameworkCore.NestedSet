using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects typed entity and direct-edge existence predicates without executing caller code.</summary>
public sealed class CompositionAnyConstructionTests
{
    /// <summary>One invariant path keeps both existence source types and the caller's deferred edge rule.</summary>
    /// <param name="engine">The explicit Doka dialect whose invariant paths use the failure relation.</param>
    /// <returns>A task completing after the owned provider services are disposed.</returns>
    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    public async Task EntityAndDirectEdgePredicatesKeepTheirTypedDeferredLambdas(string engine)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = new DbContextOptionsBuilder<CompositionContext>(
                ModelCompatibilityDatabase.Options<CompositionContext>(engine,
                    "Server=127.0.0.1;Database=composition_any;User ID=unused;Pooling=false"))
            .AddInterceptors(boundary, boundary.Commands);

        var services = new ServiceCollection();

        foreach (var extension in options.Options.Extensions)
        {
            extension.ApplyServices(services);
        }

        // WHY: Each dialect owns its complete provider graph instead of changing EF's process-wide
        // service cache. This test observes expression construction only, including direct-edge sources.
        await using var provider = services.BuildServiceProvider();
        await using var context = new CompositionContext(options.UseInternalServiceProvider(provider).Options);
        var hierarchy = context.NestedSet<CompositionNode>()
            .ForScope(Guid.Parse("820bc0eb-7a3c-4dd9-9cbb-87d758889237")).InTree(Guid.Empty);

        var rows = hierarchy.SelectSubtrees(node => node.Id == 1,
            (upper, lower, distance) => new { Upper = upper, Lower = lower });

        // Act
        var query = hierarchy.WherePathEdges(rows, row => row.Upper, row => row.Lower,
            (row, parent, child) => CallerAllowsEdge(parent.Id, child.Id));

        var observer = new AnyObserver();
        observer.Visit(query.Expression);

        // Assert
        Assert.Contains(observer.Calls,
            call => call.Method.GetGenericArguments()[0] == typeof(CompositionNode));

        Assert.Contains(observer.Calls,
            call => call.Method.GetGenericArguments()[0] == typeof(NestedSetQueryEdge<CompositionNode>));

        Assert.All(observer.Calls, call =>
        {
            var elementType = Assert.Single(call.Method.GetGenericArguments());
            var quoted = Assert.IsAssignableFrom<UnaryExpression>(call.Arguments[1]);
            var lambda = Assert.IsAssignableFrom<LambdaExpression>(quoted.Operand);

            Assert.Equal(ExpressionType.Quote, quoted.NodeType);
            Assert.Equal(elementType, Assert.Single(lambda.Parameters).Type);
            Assert.Equal(typeof(Func<,>).MakeGenericType(elementType, typeof(bool)), lambda.Type);
        });

        Assert.True(observer.CallerRules > 0);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>An equal-key endpoint from another named mapping fails before evaluating its caller rule.</summary>
    /// <param name="foreignAnchor">Whether the foreign endpoint is the anchor rather than the descendant.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForeignNamedEndpointIsRejectedWithoutEvaluatingTheEdgeRule(bool foreignAnchor)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var hierarchy = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var nodes = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var foreign = context.Set<CompositionGuardNode>(CompositionGuardContext.SecondMapping);
        var rows = foreignAnchor
            ? nodes.Join(foreign, node => node.Id, other => other.Id,
                (node, other) => new CompositionGuardRow { Upper = other, Lower = node })
            : nodes.Join(foreign, node => node.Id, other => other.Id,
                (node, other) => new CompositionGuardRow { Upper = node, Lower = other });

        // Act
        var error = Record.Exception(() => hierarchy.WherePathEdges(rows, row => row.Upper, row => row.Lower,
            (row, parent, child) => CallerAllowsEdge(parent.Id, child.Id)));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Rejects accidental evaluation while the rule is copied into several deferred path branches.</summary>
    private static bool CallerAllowsEdge(int parent, int child)
        => throw new InvalidOperationException($"The caller rule for edge {parent}/{child} must remain deferred.");

    /// <summary>Collects only standard predicate existence calls and the exact caller-rule marker.</summary>
    private sealed class AnyObserver : ExpressionVisitor
    {
        /// <summary>Gets the standard existence expressions retained in the completed query.</summary>
        internal List<MethodCallExpression> Calls { get; } = [];

        /// <summary>Gets the number of unevaluated caller-rule occurrences.</summary>
        internal int CallerRules { get; private set; }

        /// <inheritdoc />
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(Queryable)
                && node.Method.Name == nameof(Queryable.Any) && node.Arguments.Count == 2)
            {
                Calls.Add(node);
            }

            if (node.Method.DeclaringType == typeof(CompositionAnyConstructionTests)
                && node.Method.Name == nameof(CallerAllowsEdge))
            {
                CallerRules++;
            }

            return base.VisitMethodCall(node);
        }
    }
}

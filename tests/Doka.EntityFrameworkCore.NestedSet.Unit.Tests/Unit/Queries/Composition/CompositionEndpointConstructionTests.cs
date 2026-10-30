using System.Collections;
using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks bounded endpoint-construction work without changing deferred SQL or visibility rules.</summary>
public sealed class CompositionEndpointConstructionTests
{
    /// <summary>A path shares one source-wide scan, but a later operation establishes its own proof.</summary>
    /// <param name="operations">The independently constructed path operations over the same source.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void SourceVisibilityIsCheckedOncePerOperation(int operations)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var tree = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.ScopedMapping)
            .ForScope(1).InTree(Guid.Empty);

        var counter = new CountingPredicate();
        var node = Expression.Parameter(typeof(CompositionGuardNode), "node");
        var source = tree.Nodes.Where(Expression.Lambda<Func<CompositionGuardNode, bool>>(counter, node));
        IQueryable<CompositionGuardNode>? query = null;

        // Act
        for (var index = 0; index < operations; index++)
        {
            query = tree.WherePathEdges(source, value => value, value => value,
                (_, parent, child) => true);
        }

        // Assert
        Assert.Equal(operations, counter.Visits);
        Assert.NotNull(query?.Expression);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>An inner endpoint does not inspect a captured query used only by the outer payload leg.</summary>
    /// <param name="shape">The result selector whose inner endpoint determines the mapping.</param>
    [Theory]
    [InlineData("join")]
    [InlineData("select-many")]
    public void UnselectedPayloadQueryExpressionIsNotRead(string shape)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var hierarchy = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var nodes = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var payloads = context.Set<CompositionGuardNode>(CompositionGuardContext.SecondMapping);
        var trap = new QueryExpressionTrap(nodes);
        IQueryable<CompositionGuardNode> captured = trap;
        var unselected = payloads.SelectMany(payload => captured, (payload, ignored) => payload);
        var source = shape switch
        {
            "join" => unselected.Join(nodes, payload => payload.Id, node => node.Id,
                (payload, node) => new CompositionGuardRow { Upper = node, Lower = node, Payload = payload }),
            "select-many" => unselected.SelectMany(payload => nodes,
                (payload, node) => new CompositionGuardRow { Upper = node, Lower = node, Payload = payload }),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        // Act
        var query = hierarchy.WhereAnyAncestorOrSelf(source, row => row.Lower, (_, owner) => true);

        // Assert
        Assert.NotNull(query.Expression);
        Assert.Equal(0, trap.ExpressionReads);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Skipping payload alias resolution never skips a query-wide filter modifier on that leg.</summary>
    /// <param name="ignoreFilters">Whether the unrelated outer payload disables global filters.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SkippedPayloadLegStillParticipatesInVisibilityProof(bool ignoreFilters)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var tree = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.ScopedMapping)
            .ForScope(1).InTree(Guid.Empty);

        var nodes = tree.Nodes;
        IQueryable<CompositionGuardNode> payloads = context.Set<CompositionGuardNode>(
            CompositionGuardContext.SecondMapping);

        if (ignoreFilters)
        {
            payloads = payloads.IgnoreQueryFilters();
        }

        var source = payloads.Join(nodes, payload => payload.Id, node => node.Id,
            (payload, node) => new CompositionGuardRow { Upper = node, Lower = node, Payload = payload });

        var entityType = context.Model.FindEntityType(CompositionGuardContext.ScopedMapping)!;
        var bound = ((MethodCallExpression)nodes.Expression).Arguments[0];

        // Act
        var actual = NestedSetQueryEndpoint.Require(source, row => row.Lower, entityType, bound, "node");

        // Assert
        Assert.Equal(!ignoreFilters, actual.IsVisible);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>A shared syntax result does not turn an independently supplied endpoint into a bound alias.</summary>
    [Fact]
    public void SharedSourceVisibilityDoesNotProveAnExternalEndpoint()
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var tree = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.ScopedMapping)
            .ForScope(1).InTree(Guid.Empty);

        var nodes = tree.Nodes;
        var raw = context.Set<CompositionGuardNode>(CompositionGuardContext.ScopedMapping);
        var source = nodes.Join(raw, upper => upper.Id, lower => lower.Id,
            (upper, lower) => new CompositionGuardRow { Upper = upper, Lower = lower });

        var entityType = context.Model.FindEntityType(CompositionGuardContext.ScopedMapping)!;
        var bound = ((MethodCallExpression)nodes.Expression).Arguments[0];
        bool? sourceAllowsVisibility = null;

        // Act
        var upper = NestedSetQueryEndpoint.Require(source, row => row.Upper, entityType, bound,
            "anchor", ref sourceAllowsVisibility);

        var lower = NestedSetQueryEndpoint.Require(source, row => row.Lower, entityType, bound,
            "node", ref sourceAllowsVisibility);

        // Assert
        Assert.True(upper.IsVisible);
        Assert.False(lower.IsVisible);
        Assert.Equal(true, sourceAllowsVisibility);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>A fabricated endpoint depending on both selector legs is not accepted as either mapped alias.</summary>
    [Fact]
    public void EndpointConstructedFromBothSelectorParametersIsRejected()
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var hierarchy = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var nodes = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var payloads = context.Set<CompositionGuardNode>(CompositionGuardContext.SecondMapping);
        var source = payloads.Join(nodes, payload => payload.Id, node => node.Id,
            (payload, node) => new CompositionGuardRow
            {
                Upper = node,
                Lower = new CompositionGuardNode { Id = payload.Id + node.Id },
            });

        // Act
        var error = Record.Exception(() => hierarchy.WhereAnyAncestorOrSelf(
            source, row => row.Lower, (_, owner) => true));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Counts only the source-wide syntax proof; endpoint resolution skips the Where predicate.</summary>
    private sealed class CountingPredicate : Expression
    {
        /// <summary>Gets the number of syntax traversals reaching this source predicate.</summary>
        internal int Visits { get; private set; }

        /// <inheritdoc />
        public override ExpressionType NodeType => ExpressionType.Extension;

        /// <inheritdoc />
        public override Type Type => typeof(bool);

        /// <inheritdoc />
        protected override Expression VisitChildren(ExpressionVisitor visitor)
        {
            Visits++;

            return visitor.Visit(Constant(true));
        }
    }

    /// <summary>Exposes unwanted metadata access to a query that does not carry the selected endpoint.</summary>
    /// <param name="query">The deferred query whose expression must stay unread on an unrelated leg.</param>
    private sealed class QueryExpressionTrap(IQueryable<CompositionGuardNode> query)
        : IQueryable<CompositionGuardNode>
    {
        /// <summary>Gets the number of expression reads performed after the deferred source was assembled.</summary>
        internal int ExpressionReads { get; private set; }

        /// <inheritdoc />
        public Type ElementType => query.ElementType;

        /// <inheritdoc />
        public Expression Expression
        {
            get
            {
                ExpressionReads++;

                return query.Expression;
            }
        }

        /// <inheritdoc />
        public IQueryProvider Provider => query.Provider;

        /// <inheritdoc />
        public IEnumerator<CompositionGuardNode> GetEnumerator()
            => throw new InvalidOperationException("Endpoint construction must not enumerate a payload query.");

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

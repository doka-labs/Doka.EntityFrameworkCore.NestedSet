using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class CompositionAncestorIndexTests
{
    /// <summary>Deferred fallback retains the named hierarchy and rejects equal-shaped foreign endpoint mappings.</summary>
    /// <param name="includeSelf">Whether the selected source can be its own ancestor.</param>
    /// <param name="scoped">Whether the exact named hierarchy has an application scope.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task DeferredNamedHierarchyRetainsExactMappingAndRejectsForeignEndpoints(bool includeSelf, bool scoped)
    {
        // Arrange
        var options = ModelCompatibilityDatabase.Options<CompositionGuardContext>("MariaDb",
            "Server=127.0.0.1;Database=named_captured_hierarchy;User ID=unused;Pooling=false");

        await using var provider = SelectionServices(options);
        await using var context = new CompositionGuardContext(
            new DbContextOptionsBuilder<CompositionGuardContext>(options).UseInternalServiceProvider(provider).Options);

        var mapping = scoped ? CompositionGuardContext.ScopedMapping : CompositionGuardContext.FirstMapping;
        var tree = Guid.Parse("aeb74cb5-54e6-4d57-825d-45ad9e6d2e99");
        var hierarchy = scoped
            ? context.NestedSet<CompositionGuardNode>(mapping).ForScope(17).InTree(tree)
            : context.NestedSet<CompositionGuardNode>(mapping).InTree(tree);

        var nodes = context.Set<CompositionGuardNode>(mapping);
        var pairs = includeSelf
            ? hierarchy.SelectAncestorsOrSelf(node => nodes.Any(other => other.Id == node.Id),
                (node, owner, distance) => new CompositionGuardRow { Upper = owner, Lower = node, Distance = distance })
            : hierarchy.SelectAncestors(node => nodes.Any(other => other.Id == node.Id),
                (node, owner, distance) => new CompositionGuardRow { Upper = owner, Lower = node, Distance = distance });

        var composed = hierarchy.WherePathEdges(pairs, row => row.Upper, row => row.Lower,
            (row, parent, child) => child.Id > 0);

        var foreign = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.SecondMapping).InTree(tree);

        // Act
        var sql = composed.ToQueryString();
        var failure = Record.Exception(() => foreign.WhereAnyAncestorOrSelf(pairs,
            row => row.Lower, (row, owner) => owner.Id > 0));

        // Assert
        Assert.Contains(mapping, sql, StringComparison.Ordinal);
        Assert.DoesNotContain(CompositionGuardContext.SecondMapping, sql, StringComparison.Ordinal);
        Assert.DoesNotContain("WITH RECURSIVE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<ArgumentException>(failure);
        Assert.Contains(CompositionGuardContext.SecondMapping, failure.Message, StringComparison.Ordinal);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Deferred aliases retain membership checks for mutable captures and query-wide modifiers.</summary>
    /// <param name="shape">The embedded, mutable, getter-backed, or explicitly modified selection source.</param>
    /// <param name="visible">Whether construction proves every source modifier without evaluation.</param>
    [Theory]
    [InlineData("embedded", true)]
    [InlineData("captured", false)]
    [InlineData("getter", false)]
    [InlineData("modifier", false)]
    public async Task DeferredEndpointVisibilityDoesNotTrustUnexpandedCaptures(string shape, bool visible)
    {
        // Arrange
        var options = ModelCompatibilityDatabase.Options<CompositionGuardContext>("MySql",
            "Server=127.0.0.1;Database=deferred_visibility;User ID=unused;Pooling=false");

        await using var provider = SelectionServices(options);
        await using var context = new CompositionGuardContext(
            new DbContextOptionsBuilder<CompositionGuardContext>(options).UseInternalServiceProvider(provider).Options);

        var tree = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.FirstMapping).InTree(Guid.Empty);
        var nodes = tree.Nodes;
        var holder = new DeferredHierarchyGetter();
        var source = shape == "modifier" ? nodes.IgnoreQueryFilters() : nodes;
        Expression<Func<CompositionGuardNode, bool>> selected;

        if (shape is "embedded" or "modifier")
        {
            Expression<Func<CompositionGuardNode, CompositionGuardNode, bool>> match =
                (node, other) => other.Id == node.Id;

            var exists = Expression.Call(typeof(Queryable), nameof(Queryable.Any), [typeof(CompositionGuardNode)],
                source.Expression, Expression.Quote(Expression.Lambda<Func<CompositionGuardNode, bool>>(
                    match.Body, match.Parameters[1])));

            selected = Expression.Lambda<Func<CompositionGuardNode, bool>>(exists, match.Parameters[0]);
        }
        else
        {
            selected = shape == "getter"
                ? node => holder.Query.Any(other => other.Id == node.Id)
                : node => source.Any(other => other.Id == node.Id);
        }

        var query = tree.SelectAncestorsOrSelf(selected,
            (node, owner, distance) => new CompositionGuardRow { Upper = owner, Lower = node, Distance = distance });

        var marker = Assert.IsAssignableFrom<MethodCallExpression>(query.Expression);
        var entity = context.Model.FindEntityType(CompositionGuardContext.FirstMapping)!;

        // Act
        var actual = NestedSetQueryEndpoint.Require(query, row => row.Lower, entity, marker.Arguments[0], "node");

        // Assert
        Assert.True(NestedSetDeferredQuery.IsMarker(marker));
        Assert.Equal(visible, actual.IsVisible);
        Assert.Equal(0, holder.Reads);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Fails if construction evaluates an application-owned deferred hierarchy getter.</summary>
    private sealed class DeferredHierarchyGetter
    {
        /// <summary>Gets the number of attempted getter calls without retaining any context.</summary>
        internal int Reads { get; private set; }

        /// <summary>Gets a deliberately unavailable query whose getter must stay deferred.</summary>
        internal IQueryable<CompositionGuardNode> Query
        {
            get
            {
                Reads++;

                throw new InvalidOperationException("Deferred hierarchy getter must not run during provenance proof.");
            }
        }
    }
}

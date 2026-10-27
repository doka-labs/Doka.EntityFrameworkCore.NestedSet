using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;
using Doka.EntityFrameworkCore.NestedSet.Providers;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Preserves deferred caller selection and ordinary ancestor translation for both Doka dialects.</summary>
public sealed class CompositionAncestorConstructionTests
{
    /// <summary>Owned coordinate roots retain endpoint proof without trusting captured caller queries.</summary>
    /// <param name="engine">The explicitly configured Doka dialect, without server discovery.</param>
    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    public async Task StrictAncestorCoordinatesKeepDirectQueryRootsAndEndpointProof(string engine)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = new DbContextOptionsBuilder<CompositionContext>(
                ModelCompatibilityDatabase.Options<CompositionContext>(engine,
                    "Server=127.0.0.1;Database=composition_ancestors;User ID=unused;Pooling=false"))
            .AddInterceptors(boundary);

        var services = new ServiceCollection();

        foreach (var extension in options.Options.Extensions)
        {
            extension.ApplyServices(services);
        }

        await using var provider = services.BuildServiceProvider();
        await using var context = new CompositionContext(options.UseInternalServiceProvider(provider).Options);
        var tree = context.NestedSet<CompositionNode>()
            .ForScope(Guid.Parse("820bc0eb-7a3c-4dd9-9cbb-87d758889237")).InTree(Guid.Empty);

        var nodes = tree.Nodes;
        var entityType = context.Model.FindEntityType(typeof(CompositionNode))!;
        var bound = ((MethodCallExpression)nodes.Expression).Arguments[0];
        var sourceBindingMethod = NestedSetProviderCapabilities.Resolve(context).CompletedSourceMethod;
        var straightJoinMethod = NestedSetProviderCapabilities.Resolve(context).OrderedJoinMethod;

        // Act
        var query = tree.SelectAncestors(node => node.Id == 3,
            (source, ancestor, distance) => new { Source = source, Ancestor = ancestor, Distance = distance });

        var captures = new QueryCaptureCounter();
        captures.Visit(query.Expression);
        // WHY: Public composition supplies the validated method from the actual provider options.
        // An unknown binding must still fail provenance; the guard never trusts an operator by its name.
        var sourceProof = NestedSetQueryEndpoint.Require(query, row => row.Source, entityType, bound, "source",
            sourceBindingMethod: sourceBindingMethod, orderedJoinMethod: straightJoinMethod);

        var ancestorProof = NestedSetQueryEndpoint.Require(query, row => row.Ancestor, entityType, bound, "ancestor",
            sourceBindingMethod: sourceBindingMethod, orderedJoinMethod: straightJoinMethod);

        // Assert
        Assert.Equal(0, captures.Occurrences);
        Assert.True(sourceProof.IsVisible);
        Assert.True(ancestorProof.IsVisible);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Both Doka dialects translate ordinary ancestor relations without opening a connection.</summary>
    /// <param name="engine">The explicitly configured Doka dialect, without server discovery.</param>
    /// <param name="includeSelf">Whether the relation includes the selected source.</param>
    [Theory]
    [InlineData("MySql", false)]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", false)]
    [InlineData("MariaDb", true)]
    public async Task OrdinaryAncestorTranslationDoesNotPerformIo(string engine, bool includeSelf)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = new DbContextOptionsBuilder<CompositionContext>(
                ModelCompatibilityDatabase.Options<CompositionContext>(engine,
                    "Server=127.0.0.1;Database=composition_ancestors;User ID=unused;Pooling=false"))
            .AddInterceptors(boundary);

        var services = new ServiceCollection();

        foreach (var extension in options.Options.Extensions)
        {
            extension.ApplyServices(services);
        }

        await using var provider = services.BuildServiceProvider();
        await using var context = new CompositionContext(options.UseInternalServiceProvider(provider).Options);
        var hierarchy = context.NestedSet<CompositionNode>()
            .ForScope(Guid.Parse("820bc0eb-7a3c-4dd9-9cbb-87d758889237"));

        var query = includeSelf
            ? hierarchy.SelectAncestorsOrSelf(node => node.Id == 3,
                (source, ancestor, distance) => new
                {
                    Source = source.Id, Ancestor = ancestor.Id, Distance = distance,
                })
            : hierarchy.SelectAncestors(node => node.Id == 3,
                (source, ancestor, distance) => new
                {
                    Source = source.Id, Ancestor = ancestor.Id, Distance = distance,
                });

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("SELECT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Neither ancestor variant copies or executes the caller's selection expression.</summary>
    /// <param name="engine">The explicitly configured Doka dialect, without server discovery.</param>
    /// <param name="includeSelf">Whether the relation includes its selected source as distance zero.</param>
    /// <param name="queryBearing">Whether another deferred query supplies the source selection.</param>
    [Theory]
    [InlineData("MySql", false, false)]
    [InlineData("MySql", false, true)]
    [InlineData("MySql", true, false)]
    [InlineData("MySql", true, true)]
    [InlineData("MariaDb", false, false)]
    [InlineData("MariaDb", false, true)]
    [InlineData("MariaDb", true, false)]
    [InlineData("MariaDb", true, true)]
    public async Task AncestorExpansionRetainsExactlyOneCallerSelection(
        string engine,
        bool includeSelf,
        bool queryBearing
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = new DbContextOptionsBuilder<CompositionContext>(
                ModelCompatibilityDatabase.Options<CompositionContext>(engine,
                    "Server=127.0.0.1;Database=composition_ancestors;User ID=unused;Pooling=false"))
            .AddInterceptors(boundary);

        var services = new ServiceCollection();

        foreach (var extension in options.Options.Extensions)
        {
            extension.ApplyServices(services);
        }

        // WHY: These dialect-specific shape checks own their EF service graphs instead of expanding
        // EF's process-wide provider cache merely to inspect a deferred expression.
        await using var provider = services.BuildServiceProvider();
        await using var context = new CompositionContext(options.UseInternalServiceProvider(provider).Options);
        var hierarchy = context.NestedSet<CompositionNode>()
            .ForScope(Guid.Parse("820bc0eb-7a3c-4dd9-9cbb-87d758889237"));

        Expression<Func<CompositionNode, bool>> selected = queryBearing
            ? node => context.Set<CompositionNode>().Any(other => other.Id == node.Id && SelectedNode(other.Id))
            : node => SelectedNode(node.Id);

        // Act
        var query = includeSelf
            ? hierarchy.SelectAncestorsOrSelf(selected,
                (source, ancestor, distance) => new { Source = source, Ancestor = ancestor, Distance = distance })
            : hierarchy.SelectAncestors(selected,
                (source, ancestor, distance) => new { Source = source, Ancestor = ancestor, Distance = distance });

        var counter = new SelectionCounter();
        counter.Visit(query.Expression);

        // Assert
        Assert.Equal(1, counter.Occurrences);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Marks the caller selection and fails if deferred construction evaluates application code.</summary>
    private static bool SelectedNode(int key)
        => throw new InvalidOperationException($"Selection for node {key} must remain deferred.");

    /// <summary>Detects query-valued closure members without reading or trusting their runtime values.</summary>
    private sealed class QueryCaptureCounter : ExpressionVisitor
    {
        /// <summary>Gets the number of query-valued captures in the completed expression.</summary>
        internal int Occurrences { get; private set; }

        /// <inheritdoc />
        protected override Expression VisitMember(MemberExpression node)
        {
            if (typeof(IQueryable).IsAssignableFrom(node.Type))
            {
                Occurrences++;
            }

            return base.VisitMember(node);
        }
    }

    /// <summary>Counts only the exact application marker, including occurrences in nested queries.</summary>
    private sealed class SelectionCounter : ExpressionVisitor
    {
        /// <summary>Gets the number of caller-selection occurrences in the completed query tree.</summary>
        internal int Occurrences { get; private set; }

        /// <inheritdoc />
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(CompositionAncestorConstructionTests)
                && node.Method.Name == nameof(SelectedNode))
            {
                Occurrences++;
            }

            return base.VisitMethodCall(node);
        }
    }
}

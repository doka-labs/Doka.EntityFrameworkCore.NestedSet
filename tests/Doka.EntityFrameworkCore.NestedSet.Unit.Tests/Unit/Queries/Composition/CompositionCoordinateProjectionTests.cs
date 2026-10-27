using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks immutable structural selector reuse without sharing model, query, or request identities.</summary>
// WHY: Concurrent model-cache lookups and resizing can temporarily root weak keys from another suite.
// Observe lifetime only after those mutators finish; the collection leaves unrelated tests parallel.
[Collection("Allocation measurements")]
public sealed partial class CompositionCoordinateProjectionTests
{
    /// <summary>Repeated roots of one exact finalized mapping share only their immutable selector.</summary>
    /// <param name="mapping">The scoped or unscoped property-bag mapping.</param>
    /// <param name="includeNonLeaf">Whether the generated Boolean role participates.</param>
    [Theory]
    [InlineData("ScopedCoordinates", false)]
    [InlineData("ScopedCoordinates", true)]
    [InlineData("UnscopedCoordinates", false)]
    [InlineData("UnscopedCoordinates", true)]
    public async Task ExactMappingReusesSelectorAcrossQueryRoots(
        string mapping,
        bool includeNonLeaf
    )
    {
        // Arrange
        var options = CoordinateOptions();
        await using var provider = CoordinateServices(options);
        await using var first = new CoordinateContext(options.UseInternalServiceProvider(provider).Options);
        await using var second = new CoordinateContext(options.UseInternalServiceProvider(provider).Options);

        // Act
        var original = Coordinates(first, mapping, includeNonLeaf);
        var repeated = Coordinates(first, mapping, includeNonLeaf);
        var otherRoot = Coordinates(second, mapping, includeNonLeaf);

        // Assert
        Assert.Same(first.Model, second.Model);
        Assert.Same(original.Selector, repeated.Selector);
        Assert.Same(original.Selector, otherRoot.Selector);
        Assert.NotSame(original.Query, repeated.Query);
        Assert.NotSame(original.Query.Expression, repeated.Query.Expression);
        Assert.Empty(first.ChangeTracker.Entries());
        Assert.Empty(second.ChangeTracker.Entries());
    }

    /// <summary>Shared CLR types, different models, and source/candidate modes cannot borrow selectors.</summary>
    /// <param name="boundary">The identity that must keep independently configured roles separate.</param>
    [Theory]
    [InlineData("named-mapping")]
    [InlineData("model")]
    [InlineData("nonleaf-mode")]
    public async Task IndependentCoordinateIdentitiesNeverShareSelectors(string boundary)
    {
        // Arrange
        var options = CoordinateOptions();
        await using var firstProvider = CoordinateServices(options);
        await using var secondProvider = CoordinateServices(options);
        await using var first = new CoordinateContext(options.UseInternalServiceProvider(firstProvider).Options);
        await using var second = new CoordinateContext(options.UseInternalServiceProvider(secondProvider).Options);
        var original = Coordinates(first, CoordinateContext.Scoped, includeNonLeaf: false);

        // Act
        var other = boundary switch
        {
            "named-mapping" => Coordinates(first, CoordinateContext.Unscoped, includeNonLeaf: false),
            "model" => Coordinates(second, CoordinateContext.Scoped, includeNonLeaf: false),
            "nonleaf-mode" => Coordinates(first, CoordinateContext.Scoped, includeNonLeaf: true),
            _ => throw new ArgumentOutOfRangeException(nameof(boundary)),
        };

        // Assert
        Assert.NotSame(original.Selector, other.Selector);
        Assert.NotSame(first.Model, second.Model);
    }

    /// <summary>Coordinate access uses constant EF markers and retains the established SQL column order.</summary>
    /// <param name="mapping">The independently named property-bag mapping.</param>
    /// <param name="includeNonLeaf">Whether candidate selection consumes the generated flag.</param>
    [Theory]
    [InlineData("ScopedCoordinates", false)]
    [InlineData("ScopedCoordinates", true)]
    [InlineData("UnscopedCoordinates", false)]
    [InlineData("UnscopedCoordinates", true)]
    public async Task CoordinateRolesKeepExactMappingsWithoutClosureValues(
        string mapping,
        bool includeNonLeaf
    )
    {
        // Arrange
        var options = CoordinateOptions();
        await using var provider = CoordinateServices(options);
        await using var context = new CoordinateContext(options.UseInternalServiceProvider(provider).Options);
        var scoped = mapping == CoordinateContext.Scoped;
        var names = scoped
            ? new[] { "NodeId", "TreeId", "TenantId", "Start", "End", "HasChildren" }
            : ["Key", "Tree", "Left", "Right", "Branches"];

        // Act
        var selected = Coordinates(context, mapping, includeNonLeaf);
        var body = Assert.IsType<MemberInitExpression>(selected.Selector.Body);
        var calls = body.Bindings.Cast<MemberAssignment>()
            .Select(binding => Assert.IsAssignableFrom<MethodCallExpression>(binding.Expression)).ToArray();

        // Assert
        Assert.Equal(includeNonLeaf ? names : names[..^1], calls.Select(call =>
            Assert.IsType<ConstantExpression>(call.Arguments[1]).Value));
        Assert.All(calls, call =>
        {
            Assert.Equal(typeof(EF), call.Method.DeclaringType);
            Assert.Equal(nameof(EF.Property), call.Method.Name);
            Assert.Same(selected.Selector.Parameters[0], call.Arguments[0]);
        });

        Assert.Equal(scoped, body.Bindings.Any(binding => binding.Member.Name == "Scope"));
        Assert.Equal(includeNonLeaf, body.Bindings.Any(binding => binding.Member.Name == "NonLeaf"));
        Assert.Equal(typeof(object), calls[0].Type);
        Assert.Equal(typeof(object), calls[1].Type);
        Assert.Contains("SELECT", selected.Query.ToQueryString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>A cached expression must not retain its caller's disposed context or finalized model.</summary>
    [Fact]
    public void CoordinateCacheDoesNotRootContextOrModel()
    {
        // Arrange
        var weak = BuildCollectibleCoordinates();

        // Act
        CollectCoordinateGarbage();

        // Assert
        Assert.All(weak, reference => Assert.False(reference.IsAlive));
    }

    /// <summary>The lifetime probe still detects a deliberately retained model and its entity metadata.</summary>
    [Fact]
    public void CoordinateCacheLifetimeProbeDetectsRetainedModel()
    {
        // Arrange
        var weak = BuildCollectibleCoordinates(out var retainedModel);

        // Act
        CollectCoordinateGarbage();

        // Assert
        Assert.False(weak[0].IsAlive);
        Assert.True(weak[1].IsAlive);
        Assert.True(weak[2].IsAlive);
        GC.KeepAlive(retainedModel);
    }

    /// <summary>Concurrent first access publishes a single immutable selector for the exact mapping.</summary>
    [Fact]
    public async Task ColdCoordinatePublicationReturnsOneSelector()
    {
        // Arrange
        var options = CoordinateOptions();
        await using var provider = CoordinateServices(options);
        await using var context = new CoordinateContext(options.UseInternalServiceProvider(provider).Options);
        var mapping = context.Model.FindEntityType(CoordinateContext.Scoped)!;
        var roots = Enumerable.Range(0, 16).Select(_ =>
            new NestedSetQuery<Dictionary<string, object>>(context, mapping)).ToArray();

        var source = context.Set<Dictionary<string, object>>(CoordinateContext.Scoped).AsQueryable();

        // Act
        var selectors = await Task.WhenAll(roots.Select(root => Task.Run(
            () => CoordinateSelection(root, source, includeNonLeaf: true).Selector, CancellationToken.None)));

        // Assert
        Assert.All(selectors, selector => Assert.Same(selectors[0], selector));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Cached structure leaves each source's request predicate and parameter values independent.</summary>
    [Fact]
    public async Task CoordinateReuseNeverReusesCallerSelection()
    {
        // Arrange
        var options = CoordinateOptions();
        await using var provider = CoordinateServices(options);
        await using var context = new CoordinateContext(options.UseInternalServiceProvider(provider).Options);
        var entity = context.Model.FindEntityType(CoordinateContext.Scoped)!;
        var root = new NestedSetQuery<Dictionary<string, object>>(context, entity);
        var firstValue = 3;
        var secondValue = 9;
        var first = context.Set<Dictionary<string, object>>(CoordinateContext.Scoped)
            .Where(node => EF.Property<int>(node, "NodeId") == firstValue);

        var second = context.Set<Dictionary<string, object>>(CoordinateContext.Scoped)
            .Where(node => EF.Property<int>(node, "NodeId") == secondValue);

        // Act
        var original = CoordinateSelection(root, first, includeNonLeaf: false);
        var independent = CoordinateSelection(root, second, includeNonLeaf: false);

        // Assert
        Assert.Same(original.Selector, independent.Selector);
        Assert.Same(first.Expression, ((MethodCallExpression)original.Query.Expression).Arguments[0]);
        Assert.Same(second.Expression, ((MethodCallExpression)independent.Query.Expression).Arguments[0]);
        Assert.NotEqual(original.Query.ToQueryString(), independent.Query.ToQueryString());
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Shadow roles and native conversions remain translation markers, never CLR accessors.</summary>
    /// <param name="includeNonLeaf">Whether the Boolean generated role is needed.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShadowCoordinateSelectorsRemainServerSide(bool includeNonLeaf)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = CoordinateOptions().AddInterceptors(boundary);
        await using var provider = CoordinateServices(options);
        await using var context = new CoordinateContext(options.UseInternalServiceProvider(provider).Options);
        var entity = context.Model.FindEntityType(typeof(CoordinateNode))!;
        var root = new NestedSetQuery<CoordinateNode>(context, entity);
        var nodes = context.Set<CoordinateNode>().AsQueryable();

        // Act
        var selected = CoordinateSelection(root, nodes, includeNonLeaf);
        var sql = selected.Query.ToQueryString();

        // Assert
        Assert.Contains("\"TreeId\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"Left\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"Right\"", sql, StringComparison.Ordinal);
        Assert.Equal(typeof(string), entity.FindProperty("TreeId")!.GetTypeMapping().Converter!.ProviderClrType);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}

using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class CompositionCoordinateProjectionTests
{
    /// <summary>Retains projection parameter identity when names collide or some arguments are unused.</summary>
    /// <param name="ancestors">Whether the result's source is the structural lower endpoint.</param>
    /// <param name="shape">The caller's payload or unused projection argument.</param>
    [Theory]
    [InlineData(false, "all")]
    [InlineData(true, "all")]
    [InlineData(false, "unused-source")]
    [InlineData(true, "unused-source")]
    [InlineData(false, "unused-related")]
    [InlineData(true, "unused-related")]
    [InlineData(false, "unused-distance")]
    [InlineData(true, "unused-distance")]
    [InlineData(false, "nested-collection")]
    [InlineData(true, "nested-collection")]
    public void DirectProjectionKeepsSourceRelatedAndPositiveDistance(
        bool ancestors,
        string shape
    )
    {
        // Arrange
        var options = new DbContextOptionsBuilder<CompositionGuardContext>()
            .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets());

        using var provider = CoordinateServices(options.Options);
        using var context = new CompositionGuardContext(options.UseInternalServiceProvider(provider).Options);
        var entity = context.Model.FindEntityType(CompositionGuardContext.FirstMapping)!;
        var root = new NestedSetQuery<CompositionGuardNode>(context, entity);
        var upper = new CompositionGuardNode { Id = 20, Depth = 2 };
        var lower = new CompositionGuardNode { Id = 70, Depth = 7 };
        var pairs = new[] { new CoordinatePair { Upper = upper, Lower = lower } }.AsQueryable();
        var source = Expression.Parameter(typeof(CompositionGuardNode), "node");
        var related = Expression.Parameter(typeof(CompositionGuardNode), "node");
        var distance = Expression.Parameter(typeof(int), "node");
        Expression<Func<CompositionGuardNode, CompositionGuardNode, int, int[]>> template = shape switch
        {
            "all" => (first, second, depth) => new[] { first.Id, second.Id, depth },
            "unused-source" => (first, second, depth) => new[] { -1, second.Id, depth },
            "unused-related" => (first, second, depth) => new[] { first.Id, -1, depth },
            "unused-distance" => (first, second, depth) => new[] { first.Id, second.Id, -1 },
            "nested-collection" => (first, second, depth) =>
                new[] { first.Id, second.Id, depth }.Select(value => value + depth).ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        var projection = Expression.Lambda<Func<CompositionGuardNode, CompositionGuardNode, int, int[]>>(
            NestedSetQueryExpressions.Inline(template, source, related, distance), source, related, distance);

        var method = root.GetType().GetMethod("ProjectPairs", BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(typeof(CoordinatePair), typeof(int[]));

        Expression<Func<CoordinatePair, CompositionGuardNode>> upperSelector = pair => pair.Upper;
        Expression<Func<CoordinatePair, CompositionGuardNode>> lowerSelector = pair => pair.Lower;

        var expected = new[]
        {
            shape == "unused-source" ? -1 : ancestors ? lower.Id : upper.Id,
            shape == "unused-related" ? -1 : ancestors ? upper.Id : lower.Id,
            shape == "unused-distance" ? -1 : 5,
        };

        if (shape == "nested-collection")
        {
            expected = expected.Select(value => value + 5).ToArray();
        }

        // Act
        var query = Assert.IsAssignableFrom<IQueryable<int[]>>(method.Invoke(root,
            [pairs, upperSelector, lowerSelector, projection, ancestors]));

        var result = query.Single();

        // Assert
        Assert.Equal(expected, result);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Shadow structure and converted identities leave mapped collections deferred and translatable.</summary>
    /// <param name="includeSelf">Whether the structural query includes the selected node.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShadowStructurePreservesDeferredCollectionProjection(bool includeSelf)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = CoordinateOptions().AddInterceptors(boundary);
        await using var provider = CoordinateServices(options);
        await using var context = new CoordinateContext(options.UseInternalServiceProvider(provider).Options);
        var hierarchy = context.NestedSet<CoordinateNode>().InTree(Guid.Empty);

        // Act
        var query = includeSelf
            ? hierarchy.SelectAncestorsOrSelf(node => node.Id == 3,
                (source, ancestor, distance) => new
                {
                    source.Id,
                    OwnerId = ancestor.Id,
                    Distance = distance,
                    Items = ancestor.Items.Select(item => item.Id).ToList(),
                })
            : hierarchy.SelectAncestors(node => node.Id == 3,
                (source, ancestor, distance) => new
                {
                    source.Id,
                    OwnerId = ancestor.Id,
                    Distance = distance,
                    Items = ancestor.Items.Select(item => item.Id).ToList(),
                });

        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("SELECT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Retains distinct structural endpoints for direct projection semantics.</summary>
    private sealed class CoordinatePair
    {
        /// <summary>Gets the ancestor whose smaller depth defines the distance origin.</summary>
        public required CompositionGuardNode Upper { get; init; }

        /// <summary>Gets the descendant whose greater depth defines the positive distance.</summary>
        public required CompositionGuardNode Lower { get; init; }
    }
}

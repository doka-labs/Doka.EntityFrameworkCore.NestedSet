using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Distinguishes relational anchor dependencies from unrelated scalar member names.</summary>
public sealed class CompositionSelectionTests
{
    /// <summary>Ordinary scalar selection never changes strategy solely because of a property name.</summary>
    /// <param name="shape">The scalar expression or actual row-dependent collection query.</param>
    /// <param name="expected">Whether the expression contains a relational selection dependency.</param>
    [Theory]
    [InlineData("ScalarCount", false)]
    [InlineData("ScalarLength", false)]
    [InlineData("TextLength", false)]
    [InlineData("CapturedCount", false)]
    [InlineData("CapturedLength", false)]
    [InlineData("CapturedCountMethod", false)]
    [InlineData("CapturedAnyValue", false)]
    [InlineData("CapturedContains", false)]
    [InlineData("NavigationCount", true)]
    [InlineData("NavigationLength", true)]
    [InlineData("NavigationAny", true)]
    [InlineData("RelationalAny", true)]
    public void OnlyCollectionAndQueryDependenciesBypassTheSelectionBoundary(
        string shape,
        bool expected
    )
    {
        // Arrange
        var values = new[] { 1, 2 };
        var collection = new List<int> { 1, 2 };
        var query = Enumerable.Empty<SelectionNode>().AsQueryable();
        Expression<Func<SelectionNode, bool>> predicate = shape switch
        {
            "ScalarCount" => node => node.Count > 0,
            "ScalarLength" => node => node.Length > 0,
            "TextLength" => node => node.Name.Length > 0,
            "CapturedCount" => node => collection.Count > node.Id,
            "CapturedLength" => node => values.Length > node.Id,
            "CapturedCountMethod" => node => values.Count() > node.Id,
            "CapturedAnyValue" => node => collection.Any() && node.Id > 0,
            "CapturedContains" => node => Enumerable.Contains(values, node.Id),
            "NavigationCount" => node => node.Children.Count > 0,
            "NavigationLength" => node => node.Values.Length > 0,
            "NavigationAny" => node => node.Children.Any(child => child.Id > node.Id),
            "RelationalAny" => node => query.Any(other => other.Id == node.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        // Act
        var actual = NestedSetQuerySelection.UsesQueryOperators(predicate);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Exposes deliberately colliding scalar names and genuine collection expressions.</summary>
    private sealed class SelectionNode
    {
        /// <summary>Gets the scalar key used in captured selection predicates.</summary>
        public int Id { get; init; }

        /// <summary>Gets an ordinary scalar whose name does not imply a relational operation.</summary>
        public int Count { get; init; }

        /// <summary>Gets another ordinary scalar whose name must not change query strategy.</summary>
        public int Length { get; init; }

        /// <summary>Gets a scalar string whose length is a scalar database expression.</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Gets a navigation-like collection referencing the current row.</summary>
        public List<SelectionNode> Children { get; init; } = [];

        /// <summary>Gets an array whose row-dependent length can require collection translation.</summary>
        public int[] Values { get; init; } = [];
    }
}

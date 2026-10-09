using FsCheck.Xunit;

namespace Doka.NestedSet.Tests;

/// <summary>Checks interval arithmetic and complete tree identity over generated, shrinkable inputs.</summary>
public sealed class NestedSetPropertyTests
{
    /// <summary>Valid intervals retain their paired-boundary counts across the Int64 coordinate domain.</summary>
    /// <param name="coordinate">The generated starting-coordinate input.</param>
    /// <param name="nodeCount">The generated interval-size input.</param>
    [Property(MaxTest = 2000)]
    public void ValidBoundsPreserveCounts(
        long coordinate,
        long nodeCount
    )
    {
        // Arrange
        var (left, right, count) = ValidInterval(coordinate, nodeCount);

        // Act
        var bounds = new NestedSetBounds(left, right);

        // Assert
        Assert.Equal(left, bounds.Left);
        Assert.Equal(right, bounds.Right);
        Assert.Equal(count * 2, bounds.Width);
        Assert.Equal(count - 1, bounds.DescendantCount);
        Assert.Equal(count == 1, bounds.IsLeaf);
    }

    /// <summary>Arbitrary coordinates succeed exactly when positivity, ordering, and boundary pairing hold.</summary>
    /// <param name="left">The generated left boundary, including invalid values.</param>
    /// <param name="right">The generated right boundary, including invalid values.</param>
    [Property(MaxTest = 2000)]
    public void ConstructionAcceptsExactlyValidIntervals(
        long left,
        long right
    )
    {
        // Arrange
        var ordered = left > 0 && right > left;

        // WHY: With positive ordered coordinates, subtraction cannot overflow; odd distance means paired width.
        var paired = ordered && ((right - left) % 2) == 1;

        // Act
        var exception = Record.Exception(() => new NestedSetBounds(left, right));

        // Assert
        if (!ordered)
        {
            Assert.IsType<ArgumentOutOfRangeException>(exception);
        }
        else if (!paired)
        {
            Assert.IsType<ArgumentException>(exception);
        }
        else
        {
            Assert.Null(exception);
        }
    }

    /// <summary>Containment remains strict and asymmetric for generated valid intervals.</summary>
    /// <param name="firstCoordinate">The first interval's coordinate input.</param>
    /// <param name="firstCount">The first interval's size input.</param>
    /// <param name="secondCoordinate">The second interval's coordinate input.</param>
    /// <param name="secondCount">The second interval's size input.</param>
    [Property(MaxTest = 2000)]
    public void ContainmentMatchesStrictCoordinateOrder(
        long firstCoordinate,
        long firstCount,
        long secondCoordinate,
        long secondCount
    )
    {
        // Arrange
        var first = ValidInterval(firstCoordinate, firstCount);
        var second = ValidInterval(secondCoordinate, secondCount);
        var ancestor = new NestedSetBounds(first.Left, first.Right);
        var descendant = new NestedSetBounds(second.Left, second.Right);
        var expected = first.Left < second.Left && second.Right < first.Right;

        // Act
        var contains = ancestor.Contains(descendant);
        var reversed = descendant.Contains(ancestor);
        var reflexive = ancestor.Contains(ancestor);

        // Assert
        Assert.Equal(expected, contains);
        Assert.False(contains && reversed);
        Assert.False(reflexive);
    }

    /// <summary>Identical nested coordinates establish ancestry only inside the same unscoped tree.</summary>
    /// <param name="treeId">The generated tree identity.</param>
    /// <param name="sameTree">Whether the descendant belongs to the ancestor's tree.</param>
    /// <param name="coordinate">The generated positive-coordinate input.</param>
    [Property(MaxTest = 1000)]
    public void UnscopedAncestryRequiresTreeIdentity(
        int treeId,
        bool sameTree,
        int coordinate
    )
    {
        // Arrange
        var left = 1L + (uint)coordinate;
        INestedSetNode<int, int> ancestor = new PropertyNode(1, treeId, 0, left, left + 3);
        INestedSetNode<int, int> descendant = new PropertyNode(
            2,
            sameTree ? treeId : treeId ^ 1,
            0,
            left + 1,
            left + 2);

        // Act
        var isAncestor = ancestor.IsAncestorOf(descendant);
        var isDescendant = descendant.IsDescendantOf(ancestor);

        // Assert
        Assert.Equal(sameTree, isAncestor);
        Assert.Equal(isAncestor, isDescendant);
        Assert.False(descendant.IsAncestorOf(ancestor));
    }

    /// <summary>Scoped ancestry requires both identity components even when boundaries overlap exactly.</summary>
    /// <param name="treeId">The generated tree identity.</param>
    /// <param name="scope">The generated scope identity.</param>
    /// <param name="sameTree">Whether the descendant belongs to the same tree.</param>
    /// <param name="sameScope">Whether the descendant belongs to the same scope.</param>
    [Property(MaxTest = 1000)]
    public void ScopedAncestryRequiresCompleteIdentity(
        int treeId,
        int scope,
        bool sameTree,
        bool sameScope
    )
    {
        // Arrange
        IScopedNestedSetNode<int, int, int> ancestor = new PropertyNode(1, treeId, scope, 1, 4);
        IScopedNestedSetNode<int, int, int> descendant = new PropertyNode(
            2,
            sameTree ? treeId : treeId ^ 1,
            sameScope ? scope : scope ^ 1,
            2,
            3);

        // Act
        var isAncestor = ancestor.IsAncestorOf(descendant);
        var isDescendant = descendant.IsDescendantOf(ancestor);

        // Assert
        Assert.Equal(sameTree && sameScope, isAncestor);
        Assert.Equal(isAncestor, isDescendant);
        Assert.False(descendant.IsAncestorOf(ancestor));
    }

    /// <summary>Maps arbitrary generated values to paired intervals without clipping them to Int32 coordinates.</summary>
    private static (long Left, long Right, long Count) ValidInterval(
        long coordinate,
        long nodeCount
    )
    {
        // WHY: Unsigned remainders include negative inputs without Math.Abs(long.MinValue) or discarded cases.
        var left = 1 + (long)((ulong)coordinate % (ulong)(long.MaxValue - 1));
        var maximumCount = (long.MaxValue - left + 1) / 2;
        var count = 1 + (long)((ulong)nodeCount % (ulong)maximumCount);

        return (left, left + ((count * 2) - 1), count);
    }

    /// <summary>Provides generated complete identities without coupling core properties to EF Core.</summary>
    /// <param name="Id">The node identity.</param>
    /// <param name="TreeId">The containing tree identity.</param>
    /// <param name="Scope">The containing scope identity.</param>
    /// <param name="Left">The inclusive left boundary.</param>
    /// <param name="Right">The inclusive right boundary.</param>
    private sealed record PropertyNode(
        int Id,
        int TreeId,
        int Scope,
        long Left,
        long Right
    ) : IScopedNestedSetNode<int, int, int>
    {
        /// <summary>Gets the unused level required by the node contract.</summary>
        public int Depth => 0;

        /// <summary>Gets the unused sibling position required by the node contract.</summary>
        public long Position => 0;
    }
}

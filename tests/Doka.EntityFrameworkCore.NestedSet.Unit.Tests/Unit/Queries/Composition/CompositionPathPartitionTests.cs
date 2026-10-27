using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects the actual query partition arithmetic without requiring impossible Int64-sized fixtures.</summary>
public sealed class CompositionPathPartitionTests
{
    private static readonly Func<long, long> s_width = CompileWidth();
    private static readonly Func<long, long, long> s_start = CompileStart();

    /// <summary>Empty roots and large square boundaries retain a positive finite SQL partition estimate.</summary>
    /// <param name="maximumRight">The nonnegative maximum coordinate supplied to the production template.</param>
    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(3L)]
    [InlineData(4L)]
    [InlineData(8L)]
    [InlineData(9L)]
    [InlineData(399L)]
    [InlineData(400L)]
    [InlineData(401L)]
    [InlineData(4_503_599_627_370_495L)]
    [InlineData(4_503_599_627_370_496L)]
    [InlineData(4_503_599_627_370_497L)]
    [InlineData(9_007_199_254_740_991L)]
    [InlineData(9_007_199_254_740_992L)]
    [InlineData(9_007_199_254_740_993L)]
    [InlineData(9_223_372_030_926_249_000L)]
    [InlineData(9_223_372_030_926_249_001L)]
    [InlineData(9_223_372_030_926_249_002L)]
    [InlineData(long.MaxValue)]
    public void WidthEstimateRemainsPositiveAtCoordinateBoundaries(long maximumRight)
    {
        // Arrange
        // WHY: Floating-point square-root rounding can change this cost estimate by one at huge
        // squares. Correctness requires a positive consistent width, not an exact integer square root.
        const long maximumFiniteWidth = 3_037_000_501L;

        // Act
        var width = s_width(maximumRight);

        // Assert
        Assert.InRange(width, 1, maximumFiniteWidth);
    }

    /// <summary>Modulo partitions remain exact and disjoint even where adding a width could overflow.</summary>
    /// <param name="left">The positive coordinate assigned to a partition.</param>
    /// <param name="maximumRight">The maximum visible coordinate defining the shared width.</param>
    [Theory]
    [InlineData(1L, 2L)]
    [InlineData(20L, 400L)]
    [InlineData(21L, 400L)]
    [InlineData(22L, 400L)]
    [InlineData(9_007_199_254_740_991L, 9_007_199_254_740_993L)]
    [InlineData(9_007_199_254_740_992L, 9_007_199_254_740_993L)]
    [InlineData(9_007_199_254_740_993L, 9_007_199_254_740_993L)]
    [InlineData(long.MaxValue - 1, long.MaxValue)]
    [InlineData(long.MaxValue, long.MaxValue)]
    public void PartitionsKeepTheirCompleteIntegerWindow(long left, long maximumRight)
    {
        // Arrange
        var width = s_width(maximumRight);

        // Act
        var start = s_start(left, width);
        var first = s_start(start, width);
        var last = s_start(checked(start + Math.Min(width - 1, long.MaxValue - start)), width);
        var preceding = start == 0 ? (long?)null : s_start(start - 1, width);

        // Assert
        Assert.InRange(start, 0, left);
        Assert.InRange(left - start, 0, width - 1);
        Assert.Equal(start, first);
        Assert.Equal(start, last);
        Assert.True(preceding is null || preceding < start);
    }

    /// <summary>Compiles only the production arithmetic expression, without an EF root or database access.</summary>
    private static Func<long, long> CompileWidth()
    {
        var maximum = Expression.Parameter(typeof(long), "maximum");

        return Expression.Lambda<Func<long, long>>(
            NestedSetQuery<CompositionNode>.CoordinateBlockWidth(maximum), maximum).Compile();
    }

    /// <summary>Compiles the same integer partition expression used by real endpoint joins.</summary>
    private static Func<long, long, long> CompileStart()
    {
        var left = Expression.Parameter(typeof(long), "left");
        var width = Expression.Parameter(typeof(long), "width");

        return Expression.Lambda<Func<long, long, long>>(
            NestedSetQuery<CompositionNode>.CoordinateBlockStart(left, width), left, width).Compile();
    }
}

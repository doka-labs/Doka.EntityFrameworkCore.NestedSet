namespace Doka.NestedSet.Tests;

/// <summary>Verifies boundary validation, containment, and dense-tree calculations.</summary>
public sealed class NestedSetBoundsTests
{
    /// <summary>Formats even an invalid default without evaluating validating calculated properties.</summary>
    /// <param name="format">The diagnostic formatting path used by the caller.</param>
    [Theory]
    [InlineData("Direct")]
    [InlineData("Boxed")]
    [InlineData("Interpolated")]
    public void DefaultBoundsRemainPrintable(
        string format
    )
    {
        // Arrange
        var bounds = default(NestedSetBounds);

        // Act
        var text = format switch
        {
            "Direct" or "Boxed" => bounds.ToString(),
            "Interpolated" => $"{bounds}",
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

        // Assert
        Assert.Equal("NestedSetBounds { Left = 0, Right = 0 }", text);
    }

    /// <summary>Includes the stored coordinates in a valid interval's diagnostic representation.</summary>
    [Fact]
    public void BoundsFormattingIncludesStoredCoordinates()
    {
        // Arrange
        var bounds = new NestedSetBounds(2, long.MaxValue);

        // Act
        var text = bounds.ToString();

        // Assert
        Assert.Equal("NestedSetBounds { Left = 2, Right = 9223372036854775807 }", text);
    }

    /// <summary>Verifies that one interval exposes consistent dense-tree measurements.</summary>
    /// <param name="left">The inclusive left boundary.</param>
    /// <param name="right">The inclusive right boundary.</param>
    /// <param name="width">The expected inclusive width.</param>
    /// <param name="descendants">The expected number of enclosed nodes.</param>
    /// <param name="leaf">Whether the interval represents a leaf.</param>
    [Theory]
    [InlineData(1L, 2L, 2L, 0L, true)]
    [InlineData(1L, 8L, 8L, 3L, false)]
    [InlineData(10L, 15L, 6L, 2L, false)]
    [InlineData(2L, long.MaxValue, long.MaxValue - 1, (long.MaxValue - 3) / 2, false)]
    public void ComputesDenseTreeProperties(
        long left,
        long right,
        long width,
        long descendants,
        bool leaf
    )
    {
        // Arrange
        var bounds = new NestedSetBounds(left, right);

        // Act
        var actual = (bounds.Width, bounds.DescendantCount, bounds.IsLeaf);

        // Assert
        Assert.Equal((width, descendants, leaf), actual);
    }

    /// <summary>Verifies that construction rejects nonpositive or unordered bounds.</summary>
    /// <param name="left">The invalid interval's left boundary.</param>
    /// <param name="right">The invalid interval's right boundary.</param>
    [Theory]
    [InlineData(0L, 1L)]
    [InlineData(-1L, 2L)]
    [InlineData(long.MinValue, long.MaxValue)]
    [InlineData(2L, 2L)]
    [InlineData(3L, 2L)]
    public void RejectsNonPositiveOrUnorderedBounds(
        long left,
        long right
    )
    {
        // Arrange
        var invalid = (Left: left, Right: right);

        // Act
        var exception = Record.Exception(() => new NestedSetBounds(invalid.Left, invalid.Right));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    /// <summary>Verifies that construction rejects a width that cannot contain complete boundary pairs.</summary>
    [Fact]
    public void RejectsOddWidth()
    {
        // Arrange
        var invalid = (Left: 1, Right: 3);

        // Act
        var exception = Record.Exception(() => new NestedSetBounds(invalid.Left, invalid.Right));

        // Assert
        Assert.IsType<ArgumentException>(exception);
    }

    /// <summary>Verifies that the default value cannot expose a valid width.</summary>
    [Fact]
    public void DefaultBoundsRejectWidthCalculation()
    {
        // Arrange
        var invalid = default(NestedSetBounds);

        // Act
        var exception = Record.Exception(() => invalid.Width);

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    /// <summary>Verifies that the default value cannot silently appear to be a non-leaf.</summary>
    [Fact]
    public void DefaultBoundsRejectLeafCalculation()
    {
        // Arrange
        var invalid = default(NestedSetBounds);

        // Act
        var exception = Record.Exception(() => invalid.IsLeaf);

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    /// <summary>Verifies that the default value cannot expose a descendant count.</summary>
    [Fact]
    public void DefaultBoundsRejectDescendantCalculation()
    {
        // Arrange
        var invalid = default(NestedSetBounds);

        // Act
        var exception = Record.Exception(() => invalid.DescendantCount);

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    /// <summary>Verifies that containment rejects a default candidate ancestor.</summary>
    [Fact]
    public void ContainsRejectsDefaultAncestor()
    {
        // Arrange
        var invalid = default(NestedSetBounds);
        var valid = new NestedSetBounds(1, 2);

        // Act
        var exception = Record.Exception(() => invalid.Contains(valid));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    /// <summary>Verifies that containment rejects a default candidate descendant.</summary>
    [Fact]
    public void ContainsRejectsDefaultDescendant()
    {
        // Arrange
        var valid = new NestedSetBounds(1, 2);
        var invalid = default(NestedSetBounds);

        // Act
        var exception = Record.Exception(() => valid.Contains(invalid));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    /// <summary>Verifies that containment excludes equal, disjoint, and overlapping intervals.</summary>
    /// <param name="left">The candidate descendant's left boundary.</param>
    /// <param name="right">The candidate descendant's right boundary.</param>
    /// <param name="expected">Whether the candidate is strictly contained.</param>
    [Theory]
    [InlineData(2L, 3L, true)]
    [InlineData(1L, 8L, false)]
    [InlineData(9L, 10L, false)]
    [InlineData(6L, 9L, false)]
    [InlineData(long.MaxValue - 1, long.MaxValue, false)]
    public void ContainsRequiresBothStrictInteriorBounds(
        long left,
        long right,
        bool expected
    )
    {
        // Arrange
        var ancestor = new NestedSetBounds(1, 8);
        var descendant = new NestedSetBounds(left, right);

        // Act
        var actual = ancestor.Contains(descendant);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Verifies value equality for independently constructed identical intervals.</summary>
    [Fact]
    public void BoundsHaveValueEquality()
    {
        // Arrange
        var bounds = new NestedSetBounds(1, 2);
        var equivalent = new NestedSetBounds(1, 2);

        // Act
        var equal = bounds.Equals(equivalent);

        // Assert
        Assert.True(equal);
    }
}

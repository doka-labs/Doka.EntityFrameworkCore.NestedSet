namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Guards the fixed allocation protocol independently from EF models and query complexity.</summary>
[Trait("Category", "Allocation")]
public sealed class QueryConstructionAllocationTests
{
    /// <summary>
    /// Every pair contributes in exact ABBA order, and a small permanent delta stays below its budget.
    /// </summary>
    [Fact]
    public void PairedMeasurementIncludesAllOneThousandPairsInAlternatingOrder()
    {
        // Arrange
        var order = new char[2000];
        var position = 0;
        Func<object> baseline = () =>
        {
            order[position++] = 'A';

            return new byte[128];
        };

        Func<object> expanded = () =>
        {
            order[position++] = 'B';

            return new byte[256];
        };

        // Act
        var measured = QueryConstructionAllocation.MeasurePaired(baseline, expanded);

        // Assert
        Assert.Equal(2000, position);
        Assert.Equal(string.Concat(Enumerable.Repeat("ABBA", 500)), new string(order));
        Assert.True(measured.Expanded - measured.Baseline <= 4 * 1024);
    }

    /// <summary>A continuous eight-KiB regression remains above the four-KiB incremental allocation budget.</summary>
    [Fact]
    public void PairedMeanDoesNotHideContinuousAllocationGrowth()
    {
        // Arrange
        var baselineCalls = 0;
        var expandedCalls = 0;
        Func<object> baseline = () =>
        {
            baselineCalls++;

            return Array.Empty<byte>();
        };

        Func<object> expanded = () =>
        {
            expandedCalls++;

            return new byte[8 * 1024];
        };

        // Act
        var measured = QueryConstructionAllocation.MeasurePaired(baseline, expanded);

        // Assert
        Assert.Equal(1000, baselineCalls);
        Assert.Equal(1000, expandedCalls);
        Assert.True(measured.Expanded - measured.Baseline > 4 * 1024);
    }

    /// <summary>The absolute mean counts exactly 100 complete builds and retains their allocation cost.</summary>
    [Fact]
    public void AbsoluteMeanMeasuresExactlyOneHundredCompleteBuilds()
    {
        // Arrange
        var calls = 0;
        Func<object> build = () =>
        {
            calls++;

            return new byte[1024];
        };

        // Act
        var measured = QueryConstructionAllocation.Measure(build);

        // Assert
        Assert.Equal(100, calls);
        Assert.True(measured >= 1024);
    }
}

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Guards typed key equality against per-lookup boxing while preserving custom EF comparers.</summary>
// WHY: Fixed allocation budgets use the same isolated scheduling as the other comparer measurements.
[Collection("Allocation measurements")]
public sealed class KeyComparerTests
{
    /// <summary>Verifies hot dictionary operations use the generic EF comparer without boxed keys.</summary>
    [Fact]
    public void TypedIntegerComparisonsDoNotAllocatePerLookup()
    {
        // Arrange
        var comparer = new NestedSetKeyComparer<int>(
            new ValueComparer<int>(
                (first, second) => first == second,
                value => value.GetHashCode()));

        var dictionary = new Dictionary<int, int>(comparer) { [42] = 1 };
        _ = dictionary.ContainsKey(42);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var found = 0;

        // Act
        for (var index = 0; index < 100_000; index++)
        {
            found += dictionary.ContainsKey(42) ? 1 : 0;
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        Assert.Equal(100_000, found);
        Assert.InRange(allocated, 0, 256);
    }

    /// <summary>Verifies a comparer with a different generic shape still controls the fallback equality path.</summary>
    [Fact]
    public void CustomNullableComparerFallbackRetainsItsSemantics()
    {
        // Arrange
        var comparer = new NestedSetKeyComparer<int>(
            new ValueComparer<int?>(
                (first, second) => first % 10 == second % 10,
                value => value.GetValueOrDefault() % 10));

        var dictionary = new Dictionary<int, string>(comparer) { [12] = "same-key" };

        // Act
        var value = dictionary[22];

        // Assert
        Assert.Equal("same-key", value);
    }
}

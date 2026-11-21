namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Preserves typed key equality, dictionary lookup and custom EF comparer semantics.</summary>
public sealed class KeyComparerTests
{
    /// <summary>Verifies present and absent dictionary keys through the generic EF comparer.</summary>
    [Fact]
    public void ContainsKey_TypedIntegerComparer_RetainsPresentAndMissingKeys()
    {
        // Arrange
        var comparer = new NestedSetKeyComparer<int>(
            new ValueComparer<int>(
                (first, second) => first == second,
                value => value.GetHashCode()));

        var dictionary = new Dictionary<int, int>(comparer) { [42] = 1 };

        // Act
        var present = dictionary.ContainsKey(42);
        var absent = dictionary.ContainsKey(43);

        // Assert
        Assert.True(present);
        Assert.False(absent);
    }

    /// <summary>Verifies a comparer with a different generic shape still controls the fallback equality path.</summary>
    [Fact]
    public void GetItem_CustomNullableComparer_RetainsConfiguredEquality()
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

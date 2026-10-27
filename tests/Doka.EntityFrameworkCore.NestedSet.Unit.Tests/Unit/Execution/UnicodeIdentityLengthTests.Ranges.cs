namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class UnicodeIdentityLengthTests
{
    /// <summary>Every BMP code unit outside the surrogate range counts once, including its adjacent boundaries.</summary>
    [Fact]
    public void EveryNonSurrogateCodeUnitCountsOnce()
    {
        // Arrange
        var characters = Enumerable.Range(0, char.MaxValue + 1)
            .Select(value => (char)value)
            .Where(character => !char.IsSurrogate(character))
            .ToArray();

        var input = new string(characters);

        // Act
        var count = NestedSetIdentityLength.CountUnicodeScalars(input, "TreeId");

        // Assert
        Assert.Equal(63_488, count);
    }

    /// <summary>Every high or low surrogate is recognized inside a well-formed pair.</summary>
    /// <param name="varyHighSurrogate">Whether the high half rather than the low half spans its full range.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EverySurrogateCodeUnitCountsInsideValidPairs(bool varyHighSurrogate)
    {
        // Arrange
        var characters = new char[2048];

        // WHY: Fill complete pairs by ordinal so every boundary is exercised without malformed test data.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < characters.Length / 2; index++)
        {
            var offset = index * 2;
            characters[offset] = varyHighSurrogate ? (char)(0xd800 + index) : '\ud800';
            characters[offset + 1] = varyHighSurrogate ? '\udc00' : (char)(0xdc00 + index);
        }

        var input = new string(characters);

        // Act
        var count = NestedSetIdentityLength.CountUnicodeScalars(input, "TreeId");

        // Assert
        Assert.Equal(1024, count);
    }

    /// <summary>Every unpaired surrogate is rejected rather than counted or silently replaced.</summary>
    [Fact]
    public void EveryUnpairedSurrogateCodeUnitIsRejected()
    {
        // Arrange
        // WHY: Build malformed strings at runtime; attribute-string encoding cannot preserve unpaired surrogates.
        var inputs = Enumerable.Range(0xd800, 0x800)
            .Select(value => ((char)value).ToString())
            .ToArray();

        // Act
        var failures = Array.ConvertAll(inputs,
            input => Record.Exception(() => NestedSetIdentityLength.CountUnicodeScalars(input, "Scope")));

        // Assert
        Assert.All(failures, failure =>
        {
            var error = Assert.IsType<ArgumentException>(failure);

            Assert.Equal("Scope", error.ParamName);
        });
    }
}

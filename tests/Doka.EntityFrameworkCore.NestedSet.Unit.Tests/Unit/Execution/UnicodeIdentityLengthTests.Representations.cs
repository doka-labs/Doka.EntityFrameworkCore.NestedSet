namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class UnicodeIdentityLengthTests
{
    /// <summary>Counts ASCII, BMP and surrogate-pair inputs as Unicode scalars.</summary>
    /// <param name="input">Eight Unicode scalar values exercising ASCII, BMP and surrogate paths.</param>

    [Theory]
    [InlineData("abcdefgh")]
    [InlineData("\u00e4\u03a9\u4e2d\u00f1\u0416\u00e9\u3042\u20ac")]
    [InlineData("\U0001f600\U0001f601\U0001f602\U0001f603\U0001f604\U0001f605\U0001f606\U0001f607")]
    [InlineData("ab\U0001f600c\U0001f601def")]
    public void CountUnicodeScalars_UnicodeRepresentations_RetainScalarCount(
        string input
    )
    {
        // Arrange
        const int expected = 8;

        // Act
        var count = NestedSetIdentityLength.CountUnicodeScalars(input, "TreeId");

        // Assert
        Assert.Equal(expected, count);
    }
}

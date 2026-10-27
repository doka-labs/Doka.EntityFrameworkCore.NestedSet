namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class UnicodeIdentityLengthTests
{
    /// <summary>Counts characters without allocating buffers, rune collections or normalized strings.</summary>
    /// <param name="input">Eight Unicode scalar values exercising ASCII, BMP and surrogate paths.</param>
    // WHY: Coverage can add allocations inside framework helpers; validate the unchanged product budget directly.
    [Trait("Category", "Allocation")]
    [Theory]
    [InlineData("abcdefgh")]
    [InlineData("\u00e4\u03a9\u4e2d\u00f1\u0416\u00e9\u3042\u20ac")]
    [InlineData("\U0001f600\U0001f601\U0001f602\U0001f603\U0001f604\U0001f605\U0001f606\U0001f607")]
    [InlineData("ab\U0001f600c\U0001f601def")]
    public void WarmCharacterCountingDoesNotAllocate(
        string input
    )
    {
        // Arrange
        var total = 0;

        // WHY: Initialization and tiered compilation do not belong to the repeated identity-validation budget.
        for (var iteration = 0; iteration < 10_000; iteration++)
        {
            total += NestedSetIdentityLength.CountUnicodeScalars(input, "TreeId");
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        // Act
        for (var iteration = 0; iteration < 10_000; iteration++)
        {
            total += NestedSetIdentityLength.CountUnicodeScalars(input, "TreeId");
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        Assert.Equal(160_000, total);
        Assert.Equal(0, allocated);
    }
}

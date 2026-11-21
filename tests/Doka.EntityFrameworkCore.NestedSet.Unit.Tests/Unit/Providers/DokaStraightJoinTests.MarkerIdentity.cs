namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class DokaStraightJoinTests
{
    /// <summary>The selected adapter reuses its initialized public marker identity.</summary>
    [Fact]
    public void OrderedJoinMethod_RepeatedLookup_RetainsExactMarkerIdentity()
    {
        // Arrange
        var adapter = NestedSetTestAdapters.ForEngine("MySql");
        var expected = adapter.OrderedJoinMethod;

        // Act
        var actual = adapter.OrderedJoinMethod;

        // Assert
        Assert.NotNull(expected);
        Assert.Same(expected, actual);
    }
}

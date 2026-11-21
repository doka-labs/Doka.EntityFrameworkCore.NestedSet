namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class DokaSourceBindingTests
{
    /// <summary>The selected adapter reuses its initialized public marker identity.</summary>
    [Fact]
    public void CompletedSourceMethod_RepeatedLookup_RetainsExactMarkerIdentity()
    {
        // Arrange
        var adapter = NestedSetTestAdapters.ForEngine("MySql");
        var expected = adapter.CompletedSourceMethod;

        // Act
        var actual = adapter.CompletedSourceMethod;

        // Assert
        Assert.NotNull(expected);
        Assert.Same(expected, actual);
    }
}

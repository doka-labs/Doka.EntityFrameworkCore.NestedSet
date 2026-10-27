namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class DokaStraightJoinTests
{
    /// <summary>The typed ordered-join metadata does not retain sources, selectors or captured request values.</summary>
    [Fact]
    public void TypedOrderedJoinDoesNotRetainInvocationInputs()
    {
        // Arrange
        var released = ProviderQueryLifetime.ReleaseInputs(orderedJoin: true);

        // Act
        CollectReleasedInputs();

        // Assert
        Assert.All(released, reference => Assert.False(reference.IsAlive));
    }

    /// <summary>The actual public generic operator does not pin a collectible outer, inner or result type.</summary>
    /// <param name="position">The outer, inner, key or result generic argument holding the collectible type.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TypedOrderedJoinReleasesCollectibleRowArguments(int position)
    {
        // Arrange
        var released = ProviderQueryLifetime.ReleaseCollectibleRows(orderedJoin: true, position);

        // Act
        CollectReleasedInputs();

        // Assert
        Assert.All(released, reference => Assert.False(reference.IsAlive));
    }

    /// <summary>Completes finalization and a compacting collection outside the input creator's JIT lifetime.</summary>
    private static void CollectReleasedInputs()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }
}

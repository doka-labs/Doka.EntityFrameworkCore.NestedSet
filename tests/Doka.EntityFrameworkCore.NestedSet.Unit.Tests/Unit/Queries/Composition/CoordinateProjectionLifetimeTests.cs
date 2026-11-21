namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks coordinate-cache weak ownership while preserving a deliberately retained model control.</summary>
[Collection("Collector-sensitive lifetimes")]
public sealed class CoordinateProjectionLifetimeTests
{
    /// <summary>A cached expression must not retain its caller's disposed context or finalized model.</summary>
    [Fact]
    public void CollectCoordinateGarbage_CoordinateCache_DoesNotRootContextOrModel()
    {
        // Arrange
        var weak = CompositionCoordinateProjectionTests.BuildCollectibleCoordinates();

        // Act
        CompositionCoordinateProjectionTests.CollectCoordinateGarbage();

        // Assert
        Assert.All(weak, reference => Assert.False(reference.IsAlive));
    }

    /// <summary>The lifetime probe still detects a deliberately retained model and its entity metadata.</summary>
    [Fact]
    public void CollectCoordinateGarbage_CoordinateCacheLifetimeProbe_DetectsRetainedModel()
    {
        // Arrange
        var weak = CompositionCoordinateProjectionTests.BuildCollectibleCoordinates(out var retainedModel);

        // Act
        CompositionCoordinateProjectionTests.CollectCoordinateGarbage();

        // Assert
        Assert.False(weak[0].IsAlive);
        Assert.True(weak[1].IsAlive);
        Assert.True(weak[2].IsAlive);
        GC.KeepAlive(retainedModel);
    }
}

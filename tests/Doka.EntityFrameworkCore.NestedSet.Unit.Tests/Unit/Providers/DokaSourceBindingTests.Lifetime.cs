namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class DokaSourceBindingTests
{
    /// <summary>Static typed source metadata does not retain an invocation's query, provider or captured values.</summary>
    [Fact]
    public void TypedSourceDoesNotRetainInvocationInputs()
    {
        // Arrange
        var released = ProviderQueryLifetime.ReleaseInputs(orderedJoin: false);

        // Act
        CollectReleasedInputs();

        // Assert
        Assert.All(released, reference => Assert.False(reference.IsAlive));
    }

    /// <summary>The actual provider's generic method metadata does not pin a collectible source/result row.</summary>
    /// <param name="position">Whether the collectible type is the source or result generic argument.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void TypedSourceReleasesCollectibleRowArguments(int position)
    {
        // Arrange
        var released = ProviderQueryLifetime.ReleaseCollectibleRows(orderedJoin: false, position);

        // Act
        CollectReleasedInputs();

        // Assert
        Assert.All(released, reference => Assert.False(reference.IsAlive));
    }

    /// <summary>Completes finalization and a compacting collection after the no-inline creator returns.</summary>
    private static void CollectReleasedInputs()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }
}

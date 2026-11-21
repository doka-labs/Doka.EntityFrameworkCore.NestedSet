namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks collectible adapter ownership with released and deliberately retained registration roots.</summary>
[Collection("Collector-sensitive lifetimes")]
public sealed class AdapterRegistrationLifetimeTests
{
    /// <summary>The immutable registration does not globally retain a collectible adapter or its options.</summary>

    // WHY: Coverage 18.11.0 prevents collectible-module unloading even in an independent control.
    // The required uninstrumented collector-sensitive partition observes the application's actual GC lifetime.
    [Fact]
    public void UseNestedSets_CollectibleAdapterRegistration_DoesNotRetainAdapter()
    {
        // Arrange
        var released = OptionsExtensionTests.RegisterAndReleaseForeignAdapter();

        // Act
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        // Assert
        Assert.All(released, reference => Assert.False(reference.IsAlive));
    }

    /// <summary>A deliberately live registration keeps its adapter reachable, validating the lifetime probe.</summary>
    [Fact]
    public void UseNestedSets_LiveCollectibleAdapterRegistration_RetainsAdapter()
    {
        // Arrange
        var registration = OptionsExtensionTests.RegisterForeignAdapter(out var retained);

        // Act
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        // Assert
        Assert.All(retained, reference => Assert.True(reference.IsAlive));
        GC.KeepAlive(registration);
    }
}

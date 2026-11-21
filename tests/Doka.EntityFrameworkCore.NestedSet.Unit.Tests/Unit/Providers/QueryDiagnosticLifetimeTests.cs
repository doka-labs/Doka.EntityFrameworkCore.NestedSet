namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks that the weak diagnostic once-marker does not retain a disposed context.</summary>
[Collection("Collector-sensitive lifetimes")]
public sealed class QueryDiagnosticLifetimeTests
{
    /// <summary>The weak once-marker cannot extend the lifetime of its disposed context.</summary>
    [Fact]
    public void AncestorIndexWithoutReader_DisposedContext_IsNotRetained()
    {
        // Arrange
        var released = DokaQueryFeaturesTests.ReportAndReleaseContext();

        // Act
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        // Assert
        Assert.False(released.IsAlive);
    }
}

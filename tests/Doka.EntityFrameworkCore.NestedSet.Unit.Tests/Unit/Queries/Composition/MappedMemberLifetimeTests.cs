namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks model/member weak ownership independently of collector-instrumented query tests.</summary>
[Collection("Collector-sensitive lifetimes")]
public sealed class MappedMemberLifetimeTests
{
    /// <summary>
    /// Construction does not extend model or alias lifetime; a retained model is the positive control.
    /// </summary>
    /// <param name="retainModel">Whether the caller deliberately keeps the model strongly reachable.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollectGarbage_Construction_DoesNotRootModelsOrQueryAliases(
        bool retainModel
    )
    {
        // Arrange
        var witnesses = MappedMemberConstructionTests.PopulateWeakMembers(retainModel, out var retained);

        // Act
        MappedMemberConstructionTests.CollectGarbage();

        // Assert
        Assert.Equal(retainModel, witnesses[0].IsAlive);
        Assert.Equal(retainModel, witnesses[1].IsAlive);
        Assert.Equal(retainModel, witnesses[2].IsAlive);
        Assert.False(witnesses[3].IsAlive);
        GC.KeepAlive(retained);
    }
}

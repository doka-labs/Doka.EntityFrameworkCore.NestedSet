namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class AncestorIndexTests
{
    /// <summary>Owns independent model services and releases them through either context-disposal contract.</summary>
    /// <param name="asynchronous">Whether to exercise asynchronous rather than explicitly synchronous disposal.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VariantModelsOwnServicesAndReleaseThemWithContext(bool asynchronous)
    {
        // Arrange
        await using var first = new AncestorContext("Sqlite", model =>
            Configure(model).HasNestedSet(builder => builder.HasAncestorIndex("FirstFlag")));

        await using var second = new AncestorContext("Sqlite", model =>
            Configure(model).HasNestedSet(builder => builder.HasAncestorIndex("SecondFlag")));

        var firstModel = first.Model;
        var secondModel = second.Model;
        var firstFactory = first.GetService<IModelCacheKeyFactory>();
        var secondFactory = second.GetService<IModelCacheKeyFactory>();
        var firstOwner = first.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!
            .InternalServiceProvider;

        // Act
        if (asynchronous)
        {
            await first.DisposeAsync();
        }
        else
        {
            // WHY: The synchronous context contract must release its external provider too.
            first.Dispose();
        }

        var disposed = Record.Exception(() => firstOwner?.GetService(typeof(IModelSource)));

        // Assert
        Assert.NotNull(firstOwner);
        Assert.IsType<TestModelCacheKeyFactory>(firstFactory);
        Assert.IsType<TestModelCacheKeyFactory>(secondFactory);
        Assert.NotSame(firstFactory, secondFactory);
        Assert.NotSame(firstModel, secondModel);
        Assert.NotNull(firstModel.FindEntityType(typeof(AncestorNode))!.FindProperty("FirstFlag"));
        Assert.Null(firstModel.FindEntityType(typeof(AncestorNode))!.FindProperty("SecondFlag"));
        Assert.NotNull(second.Model.FindEntityType(typeof(AncestorNode))!.FindProperty("SecondFlag"));
        Assert.Null(second.Model.FindEntityType(typeof(AncestorNode))!.FindProperty("FirstFlag"));
        Assert.IsType<ObjectDisposedException>(disposed);
        Assert.Equal(ConnectionState.Closed, second.Database.GetDbConnection().State);
    }
}

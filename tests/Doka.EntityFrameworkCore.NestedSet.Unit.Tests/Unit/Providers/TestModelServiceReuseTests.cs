namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Guards against duplicating singleton provider graphs for equivalent test-only model-key algorithms.</summary>
public sealed class TestModelServiceReuseTests
{
    /// <summary>Verifies different test families reuse EF services while their actual models stay independent.</summary>
    [Fact]
    public async Task VariantTestFamiliesShareServicesWithoutSharingTheirModels()
    {
        // Arrange
        var scopeOptions = new DbContextOptionsBuilder<ScopeMappingContext>()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:")
            .UseNestedSets()
            .ReplaceService<IModelCacheKeyFactory, TestModelCacheKeyFactory>()
            .Options;

        var indexOptions = new DbContextOptionsBuilder<IndexMappingContext>()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:")
            .UseNestedSets()
            .ReplaceService<IModelCacheKeyFactory, TestModelCacheKeyFactory>()
            .Options;

        await using var scope = new ScopeMappingContext(scopeOptions, "string-matching");
        await using var index = new IndexMappingContext(indexOptions, "conventional");

        // Act
        var scopeServices = scope.GetService<IModelSource>();
        var indexServices = index.GetService<IModelSource>();

        // Assert
        Assert.Same(scopeServices, indexServices);
        Assert.Same(scope.GetService<IModelCacheKeyFactory>(), index.GetService<IModelCacheKeyFactory>());
        Assert.NotSame(scope.Model, index.Model);
        Assert.NotNull(scope.Model.FindEntityType(typeof(TextNode)));
        Assert.Null(index.Model.FindEntityType(typeof(TextNode)));
        Assert.NotNull(index.Model.FindEntityType(typeof(GuidNode)));
    }
}

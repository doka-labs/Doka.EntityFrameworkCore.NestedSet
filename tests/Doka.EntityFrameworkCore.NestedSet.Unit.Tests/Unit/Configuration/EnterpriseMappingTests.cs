namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies scoped registry mappings without application-owned lock entities.</summary>
public sealed class EnterpriseMappingTests
{
    /// <summary>A scoped hierarchy needs no tenant, workspace, or dedicated anchor entity.</summary>
    [Theory]
    [InlineData("string-default")]
    [InlineData("string-matching")]
    public void ForScope_ScopeMapping_CreatesTypedRegistryWithoutAnchor(
        string variant
    )
    {
        // Arrange
        using var context = CreateContext(variant);

        // Act
        var service = context
            .NestedSet<TextNode>()
            .ForScope("tenant");

        var registry = context
            .Model
            .GetEntityTypes()
            .Single(entity => entity.FindAnnotation(NestedSetAnnotationNames.TreeRegistryOwner) is not null);

        // Assert
        Assert.NotNull(service);
        Assert.NotNull(registry.FindProperty(NestedSetTreeRegistryMetadata.Scope));
        Assert.NotNull(registry.FindProperty(NestedSetTreeRegistryMetadata.TreeId));
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Builds one variant-aware model without opening a database connection.</summary>
    private static ScopeMappingContext CreateContext(
        string variant
    )
    {
        var options = new DbContextOptionsBuilder<ScopeMappingContext>()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets())
            .ReplaceService<IModelCacheKeyFactory, TestModelCacheKeyFactory>()
            .Options;

        return new ScopeMappingContext(options, variant);
    }
}

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies physical maintenance-key backing across supported key-configuration orders.</summary>
public sealed class EnterpriseIndexMappingTests
{
    /// <summary>Verifies that supported mappings provide a unique key over scope and node identity.</summary>
    /// <param name="variant">The key-discovery order to exercise.</param>
    [Theory]
    [InlineData("conventional")]
    [InlineData("configured")]
    [InlineData("explicit-before-ef-key")]
    [InlineData("automatic-after-ef-key")]
    public void FinalizeModel_MaintenanceKey_CoversScopeAndNodeIdentity(
        string variant
    )
    {
        // Arrange
        var options = new DbContextOptionsBuilder<IndexMappingContext>()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets())
            .ReplaceService<IModelCacheKeyFactory, TestModelCacheKeyFactory>()
            .Options;

        using var context = new IndexMappingContext(options, variant);

        // Act
        var model = context.Model;

        // Assert
        var entity = model.FindEntityType(
            variant == "conventional"
                ? typeof(GuidNode)
                : typeof(TreeNode))!;

        var nodeKey = entity.FindProperty(
            variant == "conventional"
                ? "Id"
                : "NodeId")!;

        var key = Assert.Single(
            entity.GetKeys(),
            candidate => candidate.Properties is [{ Name: "Tree" }, _] && candidate.Properties[1] == nodeKey);

        Assert.NotNull(key.GetName());
    }
}

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Keeps table resolution's property snapshot local and single-pass.</summary>
public sealed class StoreObjectEnumerationTests
{
    /// <summary>Consumes an external property sequence only once while inspecting candidate tables.</summary>
    [Fact]
    public void PropertySequenceIsSnapshottedOnce()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TreeContext>()
            .UseSqlite(
                "Data Source=:memory:",
                provider => provider.UseNestedSets())
            .Options;

        using var context = new TreeContext(options);
        var entity = context.Model.FindEntityType(typeof(TreeNode))!;
        var descriptor = NestedSetModelMapping
            .For(context.Model)
            .Descriptor(entity);

        var enumerations = 0;

        IEnumerable<IReadOnlyProperty> Properties()
        {
            if (++enumerations != 1)
            {
                throw new InvalidOperationException("The property source was enumerated more than once.");
            }

            foreach (var property in descriptor.StructuralProperties)
            {
                yield return property.Resolve(entity);
            }
        }

        // Act
        var resolved = NestedSetStoreObject.Resolve(entity, Properties());

        // Assert
        Assert.Equal(1, enumerations);
        Assert.Equal(StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()), resolved);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }
}

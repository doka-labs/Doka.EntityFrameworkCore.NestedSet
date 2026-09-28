namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects inheritance-root parent-key mapping for shared concrete TPC properties.</summary>
[Collection("Model compatibility")]
public abstract class TpcSharedCollationTests : ProviderTest
{
    /// <summary>Uses the exact provider engine for the shared inheritance model.</summary>
    /// <param name="fixture">The engine-bound compatibility fixture.</param>
    protected TpcSharedCollationTests(
        IProviderFixture<ModelCompatibilityDatabase> fixture
    ) : base(fixture) { }

    /// <summary>Scope-qualified parent keys are declared on the EF root and serve concrete TPC relationships.</summary>
    [Fact]
    public void ScopedConcreteParentKeyBelongsToInheritanceRoot()
    {
        // Arrange
        using var context = new CollatedTpcContext(ModelCompatibilityDatabase.Options<CollatedTpcContext>(Engine));

        // Act
        var model = context.Model;

        // Assert
        var root = model.FindEntityType(typeof(CollatedTpcNode))!;
        var parentKey = Assert.Single(root.GetKeys(), key => key.Properties.Count == 2);
        Assert.Equal(
            new[] { nameof(CollatedTpcNode.Scope), nameof(CollatedTpcNode.Id) },
            parentKey.Properties.Select(property => property.Name));

        Assert.Same(root, parentKey.DeclaringEntityType);
        Assert.Equal(nameof(CollatedTpcNode.Id), Assert.Single(root.FindPrimaryKey()!.Properties).Name);
        foreach (var concrete in new[] { typeof(BinaryTpcNode), typeof(CaseInsensitiveTpcNode) })
        {
            var entity = model.FindEntityType(concrete)!;
            var relationship = Assert.Single(
                entity.GetForeignKeys(),
                foreignKey => foreignKey.Properties.Any(property => property.Name == nameof(CollatedTpcNode.ParentId)));

            Assert.Same(parentKey, relationship.PrincipalKey);
            Assert.Same(entity, relationship.PrincipalEntityType);
            Assert.Equal(
                new[] { nameof(CollatedTpcNode.Scope), nameof(CollatedTpcNode.ParentId) },
                relationship.Properties.Select(property => property.Name));

            Assert.Equal(DeleteBehavior.Restrict, relationship.DeleteBehavior);
        }
    }
}

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies cached owner identity across inherited, concrete TPC, and named hierarchy metadata.</summary>
public sealed class HierarchyOwnerTests
{
    /// <summary>Shares one configured base owner and immutable ordering among concrete TPH descendants.</summary>
    [Fact]
    public void DerivedTypesReuseCachedConfiguredOwner()
    {
        // Arrange
        using var context = new OrderedTphContext(ModelCompatibilityDatabase.Options<OrderedTphContext>("Sqlite"));
        var model = context.Model;
        var owner = model.FindEntityType(typeof(InheritanceNode))!;
        var folder = model.FindEntityType(typeof(TphFolderNode))!;
        var metric = model.FindEntityType(typeof(TphMetricNode))!;

        // Act
        var mapping = NestedSetModelMapping.For(model);

        // Assert
        Assert.Same(mapping, NestedSetModelMapping.For(model));
        Assert.Same(owner, mapping.Owner(owner));
        Assert.Same(owner, mapping.Owner(folder));
        Assert.Same(owner, mapping.Owner(metric));
        Assert.Same(mapping.Descriptor(owner), mapping.Descriptor(folder));
        Assert.Same(mapping.Descriptor(owner), mapping.Descriptor(metric));
        Assert.Same(mapping.Ordering(owner), mapping.Ordering(folder));
        Assert.Same(mapping.Ordering(owner), mapping.Ordering(metric));
    }

    /// <summary>Retains independently configured concrete TPC owners despite their shared CLR base.</summary>
    [Fact]
    public void ConcreteTpcHierarchiesKeepSeparateOwners()
    {
        // Arrange
        using var context = new CollatedTpcContext(ModelCompatibilityDatabase.Options<CollatedTpcContext>("MySql"));
        var model = context.Model;
        var binary = model.FindEntityType(typeof(BinaryTpcNode))!;
        var insensitive = model.FindEntityType(typeof(CaseInsensitiveTpcNode))!;
        var unconfigured = model.FindEntityType(typeof(CollatedTpcNode))!;

        // Act
        var mapping = NestedSetModelMapping.For(model);

        // Assert
        Assert.Same(binary, mapping.Owner(binary));
        Assert.Same(insensitive, mapping.Owner(insensitive));
        Assert.NotSame(mapping.Owner(binary), mapping.Owner(insensitive));
        Assert.False(mapping.TryDescriptor(unconfigured, out _));
        Assert.Throws<InvalidOperationException>(() => mapping.Owner(unconfigured));
    }

    /// <summary>Retains the configured EF name when unrelated shared entities use the same CLR type.</summary>
    [Fact]
    public void SharedTypeOwnerKeepsExactEntityName()
    {
        // Arrange
        using var context = new SharedTypeContext(ModelCompatibilityDatabase.Options<SharedTypeContext>("Sqlite"));
        var model = context.Model;
        var configured = model.FindEntityType(SharedTypeContext.FolderEntity)!;
        var unrelated = model.FindEntityType(SharedTypeContext.UnrelatedEntity)!;

        // Act
        var mapping = NestedSetModelMapping.For(model);

        // Assert
        Assert.Equal(configured.ClrType, unrelated.ClrType);
        Assert.Same(configured, mapping.Owner(configured));
        Assert.False(mapping.TryDescriptor(unrelated, out _));
        Assert.Throws<InvalidOperationException>(() => mapping.Owner(unrelated));
    }
}

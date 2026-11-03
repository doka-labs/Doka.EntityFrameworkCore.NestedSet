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
        var repeated = NestedSetModelMapping.For(model);
        var ownerMapping = mapping.Owner(owner);
        var folderOwner = mapping.Owner(folder);
        var metricOwner = mapping.Owner(metric);
        var ownerDescriptor = mapping.Descriptor(owner);
        var folderDescriptor = mapping.Descriptor(folder);
        var repeatedOwnerDescriptor = mapping.Descriptor(owner);
        var metricDescriptor = mapping.Descriptor(metric);
        var ownerOrdering = mapping.Ordering(owner);
        var folderOrdering = mapping.Ordering(folder);
        var repeatedOwnerOrdering = mapping.Ordering(owner);
        var metricOrdering = mapping.Ordering(metric);

        // Assert
        Assert.Same(mapping, repeated);
        Assert.Same(owner, ownerMapping);
        Assert.Same(owner, folderOwner);
        Assert.Same(owner, metricOwner);
        Assert.Same(ownerDescriptor, folderDescriptor);
        Assert.Same(repeatedOwnerDescriptor, metricDescriptor);
        Assert.Same(ownerOrdering, folderOrdering);
        Assert.Same(repeatedOwnerOrdering, metricOrdering);
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
        var binaryOwner = mapping.Owner(binary);
        var insensitiveOwner = mapping.Owner(insensitive);
        var repeatedBinaryOwner = mapping.Owner(binary);
        var repeatedInsensitiveOwner = mapping.Owner(insensitive);
        var hasUnconfiguredDescriptor = mapping.TryDescriptor(unconfigured, out _);
        var error = Record.Exception(() => mapping.Owner(unconfigured));

        // Assert
        Assert.Same(binary, binaryOwner);
        Assert.Same(insensitive, insensitiveOwner);
        Assert.NotSame(repeatedBinaryOwner, repeatedInsensitiveOwner);
        Assert.False(hasUnconfiguredDescriptor);
        Assert.IsType<InvalidOperationException>(error);
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
        var configuredOwner = mapping.Owner(configured);
        var hasUnrelatedDescriptor = mapping.TryDescriptor(unrelated, out _);
        var error = Record.Exception(() => mapping.Owner(unrelated));

        // Assert
        Assert.Equal(configured.ClrType, unrelated.ClrType);
        Assert.Same(configured, configuredOwner);
        Assert.False(hasUnrelatedDescriptor);
        Assert.IsType<InvalidOperationException>(error);
    }
}

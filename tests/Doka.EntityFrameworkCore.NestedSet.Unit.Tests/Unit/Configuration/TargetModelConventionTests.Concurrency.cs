namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class TargetModelConventionTests
{
    /// <summary>Rejects an application-managed concurrency token without an explicit structural-write policy.</summary>
    [Fact]
    public void FinalizeModel_ApplicationManagedConcurrencyToken_RequiresPolicy()
    {
        // Arrange
        using var context = new TargetModelContext(entity =>
        {
            entity.HasKey(node => node.RecordId);
            entity.HasAlternateKey(node => node.NodeKey);
            entity
                .Property(node => node.Revision)
                .IsConcurrencyToken();

            Configure(entity, false, null);
        });

        // Act
        var exception = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsType<InvalidOperationException>(exception, exactMatch: false);
        Assert.Contains(nameof(TargetNode.Revision), invalid.Message, StringComparison.Ordinal);
        Assert.Contains(
            nameof(NestedSetBuilder<>.PreserveApplicationConcurrencyTokens),
            invalid.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Accepts an explicit decision to preserve application-managed tokens during structure-only writes.
    /// </summary>
    [Fact]
    public void DesignEntity_ApplicationManagedConcurrencyToken_CanBePreserved()
    {
        // Arrange
        using var context = new TargetModelContext(entity =>
        {
            entity.HasKey(node => node.RecordId);
            entity.HasAlternateKey(node => node.NodeKey);
            entity
                .Property(node => node.Revision)
                .IsConcurrencyToken();

            Configure(entity, false, builder => builder.PreserveApplicationConcurrencyTokens());
        });

        // Act
        var entity = DesignEntity(context);

        // Assert
        Assert.True(entity.FindProperty(nameof(TargetNode.Revision))!.IsConcurrencyToken);
        Assert.Equal(true, entity.FindAnnotation(NestedSetAnnotationNames.PreserveApplicationConcurrencyTokens)!.Value);
    }

    /// <summary>Accepts store-generated concurrency tokens without an application-managed-token policy.</summary>
    [Fact]
    public void DesignEntity_StoreGeneratedConcurrencyToken_DoesNotRequirePolicy()
    {
        // Arrange
        using var context = new TargetModelContext(entity =>
        {
            entity.HasKey(node => node.RecordId);
            entity.HasAlternateKey(node => node.NodeKey);
            entity
                .Property(node => node.Revision)
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();

            Configure(entity, false, null);
        });

        // Act
        var entity = DesignEntity(context);

        // Assert
        Assert.True(entity.FindProperty(nameof(TargetNode.Revision))!.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, entity.FindProperty(nameof(TargetNode.Revision))!.ValueGenerated);
    }
}

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class TargetModelConventionTests
{
    /// <summary>Rejects a scoped parent relationship whose application-owned delete behavior can cascade.</summary>
    [Fact]
    public void FinalizeModel_ScopedCascadeParentRelationship_IsRejected()
    {
        // Arrange
        using var context = new TargetModelContext(entity =>
        {
            entity.HasKey(node => node.RecordId);
            entity.HasAlternateKey(node => new
            {
                node.Scope,
                node.NodeKey,
            });

            entity
                .HasOne<TargetNode>()
                .WithMany()
                .HasForeignKey(nameof(TargetNode.Scope), nameof(TargetNode.ParentId))
                .HasPrincipalKey(nameof(TargetNode.Scope), nameof(TargetNode.NodeKey))
                .OnDelete(DeleteBehavior.Cascade);

            Configure(entity, true, null);
        });

        // Act
        var exception = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsType<InvalidOperationException>(exception, exactMatch: false);
        Assert.Contains("Restrict or NoAction", invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>Preserves an application-owned NoAction parent relationship.</summary>
    [Fact]
    public void DesignEntity_ScopedNoActionParentRelationship_IsPreserved()
    {
        // Arrange
        using var context = new TargetModelContext(entity =>
        {
            entity.HasKey(node => node.RecordId);
            entity.HasAlternateKey(node => new
            {
                node.Scope,
                node.NodeKey,
            });

            entity
                .HasOne<TargetNode>()
                .WithMany()
                .HasForeignKey(nameof(TargetNode.Scope), nameof(TargetNode.ParentId))
                .HasPrincipalKey(nameof(TargetNode.Scope), nameof(TargetNode.NodeKey))
                .OnDelete(DeleteBehavior.NoAction);

            Configure(entity, true, null);
        });

        // Act
        var entity = DesignEntity(context);
        var parents = entity
            .GetForeignKeys()
            .ToArray();

        // Assert
        var parent = Assert.Single(
            parents,
            foreignKey => foreignKey.Properties.Contains(entity.FindProperty(nameof(TargetNode.ParentId))!));

        Assert.Equal(DeleteBehavior.NoAction, parent.DeleteBehavior);
    }

    /// <summary>Rejects a scopeless Parent role already bound to another principal identity.</summary>
    [Fact]
    public void FinalizeModel_ScopelessAmbiguousParentRelationship_IsRejected()
    {
        // Arrange
        using var context = new TargetModelContext(entity =>
        {
            entity.HasKey(node => node.RecordId);
            entity.HasAlternateKey(node => node.NodeKey);
            entity.HasAlternateKey(node => node.OtherNodeKey);

            entity
                .HasOne<TargetNode>()
                .WithMany()
                .HasForeignKey(node => node.ParentId)
                .HasPrincipalKey(node => node.OtherNodeKey)
                .OnDelete(DeleteBehavior.Restrict);

            Configure(entity, false, null);
        });

        // Act
        var exception = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsType<InvalidOperationException>(exception, exactMatch: false);
        Assert.Contains("ambiguous relationship", invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>Omits the convention self-FK when key and parent collations are physically incompatible.</summary>
    [Fact]
    public void DesignEntity_IncompatibleParentCollation_OmitsConventionForeignKey()
    {
        // Arrange
        using var context = new TargetModelContext(entity =>
        {
            entity.HasKey(node => node.RecordId);
            entity.HasAlternateKey(node => node.Name);
            entity
                .Property(node => node.Name)
                .UseCollation("NOCASE");

            entity
                .Property(node => node.ParentName)
                .UseCollation("BINARY");

            entity.HasNestedSet(builder => builder
                .HasNodeKey(node => node.Name)
                .HasTreeId(node => node.TreeId)
                .HasParent(node => node.ParentName)
                .HasBounds(node => node.Left, node => node.Right)
                .HasDepth(node => node.Depth)
                .HasPosition(node => node.Position));
        });

        // Act
        var entity = DesignEntity(context);

        // Assert
        Assert.DoesNotContain(
            entity.GetForeignKeys(),
            foreignKey => foreignKey.Properties.Contains(entity.FindProperty(nameof(TargetNode.ParentName))!));

        Assert.Equal(
            4,
            entity
                .GetCheckConstraints()
                .Count());
    }
}

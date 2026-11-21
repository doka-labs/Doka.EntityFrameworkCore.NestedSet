using Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class AncestorIndexTests
{
    /// <summary>
    /// Named shared dictionaries retain their exact owner and shadow coordinates in the generated contract.
    /// </summary>
    [Fact]
    public void FindProperty_NamedSharedShadowHierarchy_KeepsExactFlagOwner()
    {
        // Arrange
        using var context = new AncestorContext(
            "Sqlite",
            model =>
            {
                ConfigureBag(model)
                    .HasNestedSet(builder => builder.HasAncestorIndex("Branch"));
            });

        // Act
        var entity = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType("NamedHierarchy")!;

        var flag = entity.FindProperty("Branch")!;

        // Assert
        Assert.Equal(
            "\"R\" > CASE WHEN \"L\" = 9223372036854775807 THEN \"L\" ELSE \"L\" + 1 END",
            flag.GetComputedColumnSql());

        Assert.Equal("NamedHierarchy", flag.DeclaringType.Name);
        Assert.Equal(typeof(bool), flag.ClrType);
        Assert.False(flag.IsNullable);
        Assert.True(flag.IsIndexerProperty());
        var index = Assert.Single(entity.GetIndexes(), index => index.Properties.Contains(flag));
        Assert.Equal(
            ["Branch", "TreeIdentity", "R", "L", "NodeId"],
            index.Properties.Select(property => property.Name));
    }

    /// <summary>
    /// Generated runtime metadata retains the opt-in scalar and bounded refresh excludes ordinary payload.
    /// </summary>
    [Fact]
    public void Descriptor_RuntimeDescriptor_RefreshesGeneratedFlagWithoutWritableStructure()
    {
        // Arrange
        using var context = new AncestorContext(
            "Sqlite",
            model =>
                Configure(model)
                    .HasNestedSet(builder => builder.HasAncestorIndex("Branch")));

        var entity = context.Model.FindEntityType(typeof(AncestorNode))!;
        var mapping = NestedSetModelMapping.For(context.Model);

        // Act
        var descriptor = mapping.Descriptor(entity);
        var refreshed = NestedSetRefreshProperties.Collect(
            entity,
            mapping.StructuralProperties(entity));

        // Assert
        Assert.Equal("Branch", descriptor.AncestorIndexProperty?.Name);
        Assert.Contains(refreshed, property => property.Name == "Branch");
        Assert.DoesNotContain(mapping.StructuralProperties(entity), property => property.Name == "Branch");
        Assert.DoesNotContain(refreshed, property => property.Name == nameof(AncestorNode.NonLeaf));
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>
    /// Late bound remapping rebuilds a library expression from final columns and preserves application indexes.
    /// </summary>
    [Fact]
    public void FindProperty_LateRoleRemapping_RebuildsOwnedExpressionAndIndex()
    {
        // Arrange
        using var context = new AncestorContext(
            "Sqlite",
            model =>
            {
                var entity = Configure(model);
                entity.HasNestedSet(builder => builder.HasAncestorIndex());
                entity
                    .Property<long>("OuterLeft")
                    .HasColumnName("outer l");

                entity
                    .Property<long>("OuterRight")
                    .HasColumnName("outer r");

                entity
                    .HasIndex(node => new
                    {
                        node.Scope,
                        node.TreeId,
                        node.Left,
                    })
                    .HasDatabaseName("ApplicationLeft");

                entity.HasNestedSet(builder => builder.HasBounds("OuterLeft", "OuterRight"));
            });

        // Act
        var entity = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(AncestorNode))!;

        var flag = entity.FindProperty("NestedSetIsNonLeaf")!;

        // Assert
        Assert.Equal(
            "\"outer r\" > CASE WHEN \"outer l\" = 9223372036854775807 THEN \"outer l\""
            + " ELSE \"outer l\" + 1 END",
            flag.GetComputedColumnSql());

        Assert.Contains(entity.GetIndexes(), index => index.GetDatabaseName() == "ApplicationLeft");
        var index = Assert.Single(entity.GetIndexes(), index => index.Properties.Contains(flag));
        Assert.Equal(
            ["Scope", "NestedSetIsNonLeaf", "TreeId", "OuterRight", "OuterLeft", "Id"],
            index.Properties.Select(property => property.Name));
    }

    /// <summary>
    /// Matching explicit computed metadata is adopted without changing its SQL or persistence policy.
    /// </summary>
    [Fact]
    public void FindProperty_MatchingApplicationComputedFlagAndIndex_AreReused()
    {
        // Arrange
        using var context = new AncestorContext(
            "Sqlite",
            model =>
            {
                var entity = Configure(model);
                entity
                    .Property<bool>("Branch")
                    .HasComputedColumnSql(
                        "\"Right\" > CASE WHEN \"Left\" = 9223372036854775807 THEN \"Left\" ELSE \"Left\" + 1 END",
                        false);

                entity.HasIndex(["Scope", "Branch", "TreeId", "Right", "Left", "Id"], "ApplicationAncestors");
                entity.HasNestedSet(builder => builder.HasAncestorIndex("Branch"));
            });

        // Act
        var entity = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(AncestorNode))!;

        var flag = entity.FindProperty("Branch")!;

        // Assert
        var index = Assert.Single(entity.GetIndexes(), index => index.Properties.Contains(flag));
        Assert.Equal("ApplicationAncestors", index.Name);
        Assert.Equal(
            "\"Right\" > CASE WHEN \"Left\" = 9223372036854775807 THEN \"Left\" ELSE \"Left\" + 1 END",
            flag.GetComputedColumnSql());

        Assert.False(flag.GetIsStored());
    }
}

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class AncestorIndexTests
{
    /// <summary>Secondary structural fragments receive the generated property and index on the same table.</summary>
    [Fact]
    public void Table_EntitySplitFlag_FollowsLateStructureFragment()
    {
        // Arrange
        using var context = new AncestorContext(
            "Sqlite",
            model =>
            {
                var entity = Configure(model);
                entity.HasNestedSet(builder => builder.HasAncestorIndex());
                entity
                    .Property<bool>("NestedSetIsNonLeaf")
                    .HasColumnName("branch flag");

                entity.ToTable("Payload");
                entity.SplitToTable(
                    "Structure",
                    split =>
                    {
                        split.Property(node => node.Scope);
                        split.Property(node => node.TreeId);
                        split.Property(node => node.Left);
                        split.Property(node => node.Right);
                        split.Property(node => node.ParentId);
                        split.Property(node => node.Depth);
                        split.Property(node => node.Position);
                    });
            });

        // Act
        var entity = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(AncestorNode))!;

        var store = StoreObjectIdentifier.Table("Structure");
        var flag = entity.FindProperty("NestedSetIsNonLeaf")!;

        // Assert
        Assert.Equal("branch flag", flag.GetColumnName(store));
        Assert.Equal(
            "\"Right\" > CASE WHEN \"Left\" = 9223372036854775807 THEN \"Left\" ELSE \"Left\" + 1 END",
            flag.GetComputedColumnSql(store));

        var index = Assert.Single(entity.GetIndexes(), index => index.Properties.Contains(flag));
        Assert.All(index.Properties, property => Assert.NotNull(property.GetColumnName(store)));
        var table = entity
            .Model
            .GetRelationalModel()
            .FindTable("Structure", null)!;

        Assert.Contains(table.Columns, column => column.Name == flag.GetColumnName(store));
    }

    /// <summary>
    /// A flag explicitly placed on another fragment fails before invalid generated SQL reaches migrations.
    /// </summary>
    [Fact]
    public void FinalizeModel_AdoptedFlagOnDifferentFragment_IsRejected()
    {
        // Arrange
        using var context = new AncestorContext(
            "Sqlite",
            model =>
            {
                var entity = Configure(model);
                entity.HasNestedSet(builder => builder.HasAncestorIndex(nameof(AncestorNode.NonLeaf)));
                entity.ToTable("Payload", table => table.Property(node => node.NonLeaf));
                entity.SplitToTable(
                    "Structure",
                    split =>
                    {
                        split.Property(node => node.Scope);
                        split.Property(node => node.TreeId);
                        split.Property(node => node.Left);
                        split.Property(node => node.Right);
                        split.Property(node => node.ParentId);
                        split.Property(node => node.Depth);
                        split.Property(node => node.Position);
                    });
            });

        // Act
        var error = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsAssignableFrom<InvalidOperationException>(error);
        Assert.Contains("application-selected table fragment", invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A later application fragment assignment adopts a library-created flag and forbids silent relocation.
    /// </summary>
    /// <param name="separateFlagFragment">Whether a third fragment adopts the flag without renaming its column.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FinalizeModel_AdoptedShadowFlagOnDifferentExplicitFragment_IsRejected(
        bool separateFlagFragment
    )
    {
        // Arrange
        using var context = new AncestorContext(
            "Sqlite",
            model =>
            {
                var entity = Configure(model);
                entity.HasNestedSet(builder => builder.HasAncestorIndex("Branch"));
                entity.Property<bool>("Branch");
                entity.ToTable("Payload");
                if (!separateFlagFragment)
                {
                    entity.ToTable(
                        "Payload",
                        table => table
                            .Property("Branch")
                            .HasColumnName("ApplicationBranch"));
                }

                entity.SplitToTable(
                    "Structure",
                    split =>
                    {
                        split.Property(node => node.Scope);
                        split.Property(node => node.TreeId);
                        split.Property(node => node.Left);
                        split.Property(node => node.Right);
                        split.Property(node => node.ParentId);
                        split.Property(node => node.Depth);
                        split.Property(node => node.Position);
                    });

                if (separateFlagFragment)
                {
                    entity.SplitToTable("Flags", split => split.Property<bool>("Branch"));
                }
            });

        // Act
        var error = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsAssignableFrom<InvalidOperationException>(error);
        Assert.Contains("application-selected table fragment", invalid.Message, StringComparison.Ordinal);
    }
}

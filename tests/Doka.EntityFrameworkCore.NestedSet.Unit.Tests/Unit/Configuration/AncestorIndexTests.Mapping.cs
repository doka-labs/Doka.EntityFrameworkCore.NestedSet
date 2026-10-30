using Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class AncestorIndexTests
{
    /// <summary>Named shared dictionaries retain their exact owner and shadow coordinates in the generated contract.</summary>
    [Fact]
    public void NamedSharedShadowHierarchyKeepsExactFlagOwner()
    {
        // Arrange
        using var context = new AncestorContext("Sqlite", model =>
        {
            ConfigureBag(model).HasNestedSet(builder => builder.HasAncestorIndex("Branch"));
        });

        // Act
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType("NamedHierarchy")!;
        var flag = entity.FindProperty("Branch")!;

        // Assert
        Assert.Equal("\"R\" > CASE WHEN \"L\" = 9223372036854775807 THEN \"L\" ELSE \"L\" + 1 END",
            flag.GetComputedColumnSql());
        Assert.Equal("NamedHierarchy", flag.DeclaringType.Name);
        Assert.Equal(typeof(bool), flag.ClrType);
        Assert.False(flag.IsNullable);
        Assert.True(flag.IsIndexerProperty());
        var index = Assert.Single(entity.GetIndexes(), index => index.Properties.Contains(flag));
        Assert.Equal(["Branch", "TreeIdentity", "R", "L", "NodeId"], index.Properties.Select(property => property.Name));
    }

    /// <summary>Generated runtime metadata retains the opt-in scalar and bounded refresh excludes ordinary payload.</summary>
    [Fact]
    public void RuntimeDescriptorRefreshesGeneratedFlagWithoutWritableStructure()
    {
        // Arrange
        using var context = new AncestorContext("Sqlite", model =>
            Configure(model).HasNestedSet(builder => builder.HasAncestorIndex("Branch")));

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
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Late bound remapping rebuilds a library expression from final columns and preserves application indexes.</summary>
    [Fact]
    public void LateRoleRemappingRebuildsOwnedExpressionAndIndex()
    {
        // Arrange
        using var context = new AncestorContext("Sqlite", model =>
        {
            var entity = Configure(model);
            entity.HasNestedSet(builder => builder.HasAncestorIndex());
            entity.Property<long>("OuterLeft").HasColumnName("outer l");
            entity.Property<long>("OuterRight").HasColumnName("outer r");
            entity.HasIndex(node => new { node.Scope, node.TreeId, node.Left }).HasDatabaseName("ApplicationLeft");
            entity.HasNestedSet(builder => builder.HasBounds("OuterLeft", "OuterRight"));
        });

        // Act
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(AncestorNode))!;
        var flag = entity.FindProperty("NestedSetIsNonLeaf")!;

        // Assert
        Assert.Equal("\"outer r\" > CASE WHEN \"outer l\" = 9223372036854775807 THEN \"outer l\""
            + " ELSE \"outer l\" + 1 END",
            flag.GetComputedColumnSql());
        Assert.Contains(entity.GetIndexes(), index => index.GetDatabaseName() == "ApplicationLeft");
        var index = Assert.Single(entity.GetIndexes(), index => index.Properties.Contains(flag));
        Assert.Equal(["Scope", "NestedSetIsNonLeaf", "TreeId", "OuterRight", "OuterLeft", "Id"], index.Properties.Select(property => property.Name));
    }

    /// <summary>Secondary structural fragments receive the generated property and index on the same table.</summary>
    [Fact]
    public void EntitySplitFlagFollowsLateStructureFragment()
    {
        // Arrange
        using var context = new AncestorContext("Sqlite", model =>
        {
            var entity = Configure(model);
            entity.HasNestedSet(builder => builder.HasAncestorIndex());
            entity.Property<bool>("NestedSetIsNonLeaf").HasColumnName("branch flag");
            entity.ToTable("Payload");
            entity.SplitToTable("Structure", split =>
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
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(AncestorNode))!;
        var store = StoreObjectIdentifier.Table("Structure", null);
        var flag = entity.FindProperty("NestedSetIsNonLeaf")!;

        // Assert
        Assert.Equal("branch flag", flag.GetColumnName(store));
        Assert.Equal("\"Right\" > CASE WHEN \"Left\" = 9223372036854775807 THEN \"Left\" ELSE \"Left\" + 1 END",
            flag.GetComputedColumnSql(store));
        var index = Assert.Single(entity.GetIndexes(), index => index.Properties.Contains(flag));
        Assert.All(index.Properties, property => Assert.NotNull(property.GetColumnName(store)));
        var table = entity.Model.GetRelationalModel().FindTable("Structure", null)!;
        Assert.Contains(table.Columns, column => column.Name == flag.GetColumnName(store));
    }

    /// <summary>A flag explicitly placed on another fragment fails before invalid generated SQL reaches migrations.</summary>
    [Fact]
    public void AdoptedFlagOnDifferentFragmentIsRejected()
    {
        // Arrange
        using var context = new AncestorContext("Sqlite", model =>
        {
            var entity = Configure(model);
            entity.HasNestedSet(builder => builder.HasAncestorIndex(nameof(AncestorNode.NonLeaf)));
            entity.ToTable("Payload", table => table.Property(node => node.NonLeaf));
            entity.SplitToTable("Structure", split =>
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

    /// <summary>Matching explicit computed metadata is adopted without changing its SQL or persistence policy.</summary>
    [Fact]
    public void MatchingApplicationComputedFlagAndIndexAreReused()
    {
        // Arrange
        using var context = new AncestorContext("Sqlite", model =>
        {
            var entity = Configure(model);
            entity.Property<bool>("Branch").HasComputedColumnSql(
                "\"Right\" > CASE WHEN \"Left\" = 9223372036854775807 THEN \"Left\" ELSE \"Left\" + 1 END", false);
            entity.HasIndex(["Scope", "Branch", "TreeId", "Right", "Left", "Id"], "ApplicationAncestors");
            entity.HasNestedSet(builder => builder.HasAncestorIndex("Branch"));
        });

        // Act
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(AncestorNode))!;
        var flag = entity.FindProperty("Branch")!;

        // Assert
        var index = Assert.Single(entity.GetIndexes(), index => index.Properties.Contains(flag));
        Assert.Equal("ApplicationAncestors", index.Name);
        Assert.Equal("\"Right\" > CASE WHEN \"Left\" = 9223372036854775807 THEN \"Left\" ELSE \"Left\" + 1 END",
            flag.GetComputedColumnSql());
        Assert.False(flag.GetIsStored());
    }

    /// <summary>Inherited configured owners retain the flag on the table containing both interval boundaries.</summary>
    /// <param name="strategy">The inheritance table strategy.</param>
    [Theory]
    [InlineData("TPH")]
    [InlineData("TPT")]
    public void InheritedOwnersKeepGeneratedFlagWithTheirBoundaries(
        string strategy
    )
    {
        // Arrange
        using var context = new AncestorContext("Sqlite", model =>
        {
            var entity = Configure(model);
            entity.ToTable("BaseNodes");
            entity.HasOne<AncestorNode>().WithMany()
                .HasForeignKey(node => new { node.Scope, node.ParentId })
                .HasPrincipalKey(node => new { node.Scope, node.Id })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasNestedSet(builder => builder.HasAncestorIndex());
            var derived = model.Entity<DerivedAncestorNode>();
            if (strategy == "TPT")
            {
                entity.UseTptMappingStrategy();
                derived.ToTable("DerivedNodes");
            }
        });

        // Act
        var baseEntity = context.Model.FindEntityType(typeof(AncestorNode))!;
        var derivedEntity = context.Model.FindEntityType(typeof(DerivedAncestorNode))!;
        var mapping = NestedSetModelMapping.For(context.Model);
        var flag = mapping.Descriptor(derivedEntity).AncestorIndexProperty!.Value.Resolve(derivedEntity);

        // Assert
        Assert.Same(baseEntity, mapping.Owner(derivedEntity));
        Assert.Equal("NestedSetIsNonLeaf", flag.GetColumnName(StoreObjectIdentifier.Table("BaseNodes", null)));
        Assert.Same(baseEntity, flag.DeclaringType);
    }

    /// <summary>Establishes that the equivalent unindexed TPT model remains an accepted baseline.</summary>
    [Fact]
    public void UnindexedInheritedOwnerRetainsValidTptRelationship()
    {
        // Arrange
        using var context = new AncestorContext("Sqlite", model =>
        {
            var entity = Configure(model);
            entity.UseTptMappingStrategy();
            entity.ToTable("BaseNodes");
            entity.HasOne<AncestorNode>().WithMany()
                .HasForeignKey(node => new { node.Scope, node.ParentId })
                .HasPrincipalKey(node => new { node.Scope, node.Id })
                .OnDelete(DeleteBehavior.Restrict);

            model.Entity<DerivedAncestorNode>().ToTable("DerivedNodes");
        });

        // Act
        var model = context.Model;

        // Assert
        Assert.Equal("BaseNodes", model.FindEntityType(typeof(AncestorNode))!.GetTableName());
        Assert.Equal("DerivedNodes", model.FindEntityType(typeof(DerivedAncestorNode))!.GetTableName());
    }

    /// <summary>A separately configured concrete TPC hierarchy derives the generated expression on its own table.</summary>
    /// <param name="lateStrategy">Whether TPC is selected after hierarchy configuration.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConcreteTpcHierarchyKeepsItsOwnAncestorIndex(
        bool lateStrategy
    )
    {
        // Arrange
        using var context = new AncestorContext("Sqlite", model =>
        {
            model.Ignore<AncestorNode>();
            if (!lateStrategy)
            {
                model.Entity<TpcAncestorNode>().UseTpcMappingStrategy();
            }

            model.Entity<TpcAncestorNode>().HasKey(node => node.Id);
            model.Entity<TpcAncestorNode>().Property(node => node.Id).ValueGeneratedNever();
            model.Entity<OtherTpcAncestorNode>().ToTable("UnconfiguredSibling");
            var entity = model.Entity<ConcreteTpcAncestorNode>().ToTable("ConcreteHierarchy");
            entity.HasNestedSet(builder => builder
                .HasScope(node => node.Scope)
                .HasTreeId(node => node.TreeId)
                .HasBounds(node => node.Left, node => node.Right)
                .HasParent(node => node.ParentId)
                .HasDepth(node => node.Depth)
                .HasPosition(node => node.Position)
                .HasAncestorIndex());

            if (lateStrategy)
            {
                model.Entity<TpcAncestorNode>().UseTpcMappingStrategy();
            }
        });

        // Act
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ConcreteTpcAncestorNode))!;
        var flag = entity.FindProperty("NestedSetIsNonLeaf")!;
        var store = StoreObjectIdentifier.Table("ConcreteHierarchy", null);

        // Assert
        Assert.Equal("NestedSetIsNonLeaf", flag.GetColumnName(store));
        Assert.Equal("\"Right\" > CASE WHEN \"Left\" = 9223372036854775807 THEN \"Left\" ELSE \"Left\" + 1 END",
            flag.GetComputedColumnSql(store));
        Assert.Null(entity.Model.FindEntityType(typeof(OtherTpcAncestorNode))!.FindProperty("NestedSetIsNonLeaf"));
        var index = Assert.Single(entity.GetIndexes(), index => index.Properties.Contains(flag));
        Assert.All(index.Properties, property => Assert.NotNull(property.GetColumnName(store)));
    }

    /// <summary>Shared tables retain one hierarchy-owned generated column without changing their payload owner.</summary>
    [Fact]
    public void TableSplitHierarchyKeepsGeneratedFlagOnSharedTable()
    {
        // Arrange
        using var context = new AncestorContext("Sqlite", model =>
        {
            var entity = Configure(model);
            entity.ToTable("SharedNodes");
            entity.HasNestedSet(builder => builder.HasAncestorIndex());
            model.Entity<AncestorPayload>().ToTable("SharedNodes");
            entity.HasOne<AncestorPayload>().WithOne().HasForeignKey<AncestorPayload>(payload => payload.Id);
        });

        // Act
        var model = context.GetService<IDesignTimeModel>().Model;
        var entity = model.FindEntityType(typeof(AncestorNode))!;
        var payload = model.FindEntityType(typeof(AncestorPayload))!;
        var flag = entity.FindProperty("NestedSetIsNonLeaf")!;

        // Assert
        Assert.Equal("NestedSetIsNonLeaf", flag.GetColumnName(StoreObjectIdentifier.Table("SharedNodes", null)));
        Assert.Null(payload.FindProperty("NestedSetIsNonLeaf"));
        Assert.Single(model.GetRelationalModel().FindTable("SharedNodes", null)!.Columns,
            column => column.Name == "NestedSetIsNonLeaf");
    }

    /// <summary>A later application fragment assignment adopts a library-created flag and forbids silent relocation.</summary>
    /// <param name="separateFlagFragment">Whether a third fragment adopts the flag without renaming its column.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AdoptedShadowFlagOnDifferentExplicitFragmentIsRejected(
        bool separateFlagFragment
    )
    {
        // Arrange
        using var context = new AncestorContext("Sqlite", model =>
        {
            var entity = Configure(model);
            entity.HasNestedSet(builder => builder.HasAncestorIndex("Branch"));
            entity.Property<bool>("Branch");
            entity.ToTable("Payload");
            if (!separateFlagFragment)
            {
                entity.ToTable("Payload", table => table.Property("Branch").HasColumnName("ApplicationBranch"));
            }

            entity.SplitToTable("Structure", split =>
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

    /// <summary>An independent TPT owner shares inherited bounds rather than generating against its payload table.</summary>
    /// <param name="lateTable">Whether the derived table is selected after hierarchy configuration.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentlyConfiguredTptOwnerKeepsFlagWithInheritedBounds(
        bool lateTable
    )
    {
        // Arrange
        using var context = new AncestorContext("Sqlite", model =>
        {
            model.Entity<AncestorNode>().HasKey(node => node.Id);
            model.Entity<AncestorNode>().ToTable("InheritedStructure");
            var entity = model.Entity<DerivedAncestorNode>();
            if (!lateTable)
            {
                entity.ToTable("DerivedPayload");
            }

            entity.HasNestedSet(builder => builder
                .HasTreeId(node => node.TreeId)
                .HasBounds(node => node.Left, node => node.Right)
                .HasParent(node => node.ParentId)
                .HasDepth(node => node.Depth)
                .HasPosition(node => node.Position)
                .HasAncestorIndex());

            if (lateTable)
            {
                entity.ToTable("DerivedPayload");
            }
        });

        // Act
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(DerivedAncestorNode))!;
        var flag = entity.FindProperty("NestedSetIsNonLeaf")!;
        var store = StoreObjectIdentifier.Table("InheritedStructure", null);

        // Assert
        Assert.Equal("NestedSetIsNonLeaf", flag.GetColumnName(store));
        Assert.Equal("\"Right\" > CASE WHEN \"Left\" = 9223372036854775807 THEN \"Left\" ELSE \"Left\" + 1 END",
            flag.GetComputedColumnSql(store));
        Assert.Contains(entity.Model.GetRelationalModel().FindTable("InheritedStructure", null)!.Columns,
            column => column.Name == "NestedSetIsNonLeaf");
    }
}

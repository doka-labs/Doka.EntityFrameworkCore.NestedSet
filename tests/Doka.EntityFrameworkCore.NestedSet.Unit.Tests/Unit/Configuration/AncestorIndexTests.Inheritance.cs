namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class AncestorIndexTests
{
    /// <summary>Inherited configured owners retain the flag on the table containing both interval boundaries.</summary>
    /// <param name="strategy">The inheritance table strategy.</param>
    [Theory]
    [InlineData("TPH")]
    [InlineData("TPT")]
    public void For_InheritedOwners_KeepGeneratedFlagWithTheirBoundaries(
        string strategy
    )
    {
        // Arrange
        using var context = new AncestorContext(
            "Sqlite",
            model =>
            {
                var entity = Configure(model);
                entity.ToTable("BaseNodes");
                entity
                    .HasOne<AncestorNode>()
                    .WithMany()
                    .HasForeignKey(node => new
                    {
                        node.Scope,
                        node.ParentId,
                    })
                    .HasPrincipalKey(node => new
                    {
                        node.Scope,
                        node.Id,
                    })
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
        var flag = mapping.Descriptor(derivedEntity)
            .AncestorIndexProperty!.Value.Resolve(derivedEntity);

        // Assert
        Assert.Same(baseEntity, mapping.Owner(derivedEntity));
        Assert.Equal("NestedSetIsNonLeaf", flag.GetColumnName(StoreObjectIdentifier.Table("BaseNodes")));
        Assert.Same(baseEntity, flag.DeclaringType);
    }

    /// <summary>Establishes that the equivalent unindexed TPT model remains an accepted baseline.</summary>
    [Fact]
    public void FinalizeModel_UnindexedInheritedOwner_RetainsValidTptRelationship()
    {
        // Arrange
        using var context = new AncestorContext(
            "Sqlite",
            model =>
            {
                var entity = Configure(model);
                entity.UseTptMappingStrategy();
                entity.ToTable("BaseNodes");
                entity
                    .HasOne<AncestorNode>()
                    .WithMany()
                    .HasForeignKey(node => new
                    {
                        node.Scope,
                        node.ParentId,
                    })
                    .HasPrincipalKey(node => new
                    {
                        node.Scope,
                        node.Id,
                    })
                    .OnDelete(DeleteBehavior.Restrict);

                model
                    .Entity<DerivedAncestorNode>()
                    .ToTable("DerivedNodes");
            });

        // Act
        var model = context.Model;

        // Assert
        Assert.Equal("BaseNodes", model.FindEntityType(typeof(AncestorNode))!.GetTableName());
        Assert.Equal("DerivedNodes", model.FindEntityType(typeof(DerivedAncestorNode))!.GetTableName());
    }

    /// <summary>
    /// A separately configured concrete TPC hierarchy derives the generated expression on its own table.
    /// </summary>
    /// <param name="lateStrategy">Whether TPC is selected after hierarchy configuration.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FindProperty_ConcreteTpcHierarchy_KeepsItsOwnAncestorIndex(
        bool lateStrategy
    )
    {
        // Arrange
        using var context = new AncestorContext(
            "Sqlite",
            model =>
            {
                model.Ignore<AncestorNode>();
                if (!lateStrategy)
                {
                    model
                        .Entity<TpcAncestorNode>()
                        .UseTpcMappingStrategy();
                }

                model
                    .Entity<TpcAncestorNode>()
                    .HasKey(node => node.Id);

                model
                    .Entity<TpcAncestorNode>()
                    .Property(node => node.Id)
                    .ValueGeneratedNever();

                model
                    .Entity<OtherTpcAncestorNode>()
                    .ToTable("UnconfiguredSibling");

                var entity = model
                    .Entity<ConcreteTpcAncestorNode>()
                    .ToTable("ConcreteHierarchy");

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
                    model
                        .Entity<TpcAncestorNode>()
                        .UseTpcMappingStrategy();
                }
            });

        // Act
        var entity = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(ConcreteTpcAncestorNode))!;

        var flag = entity.FindProperty("NestedSetIsNonLeaf")!;
        var store = StoreObjectIdentifier.Table("ConcreteHierarchy");

        // Assert
        Assert.Equal("NestedSetIsNonLeaf", flag.GetColumnName(store));
        Assert.Equal(
            "\"Right\" > CASE WHEN \"Left\" = 9223372036854775807 THEN \"Left\" ELSE \"Left\" + 1 END",
            flag.GetComputedColumnSql(store));

        Assert.Null(entity.Model.FindEntityType(typeof(OtherTpcAncestorNode))!.FindProperty("NestedSetIsNonLeaf"));
        var index = Assert.Single(entity.GetIndexes(), index => index.Properties.Contains(flag));
        Assert.All(index.Properties, property => Assert.NotNull(property.GetColumnName(store)));
    }

    /// <summary>
    /// Shared tables retain one hierarchy-owned generated column without changing their payload owner.
    /// </summary>
    [Fact]
    public void FindProperty_TableSplitHierarchy_KeepsGeneratedFlagOnSharedTable()
    {
        // Arrange
        using var context = new AncestorContext(
            "Sqlite",
            model =>
            {
                var entity = Configure(model);
                entity.ToTable("SharedNodes");
                entity.HasNestedSet(builder => builder.HasAncestorIndex());
                model
                    .Entity<AncestorPayload>()
                    .ToTable("SharedNodes");

                entity
                    .HasOne<AncestorPayload>()
                    .WithOne()
                    .HasForeignKey<AncestorPayload>(payload => payload.Id);
            });

        // Act
        var model = context.GetService<IDesignTimeModel>()
            .Model;

        var entity = model.FindEntityType(typeof(AncestorNode))!;
        var payload = model.FindEntityType(typeof(AncestorPayload))!;
        var flag = entity.FindProperty("NestedSetIsNonLeaf")!;

        // Assert
        Assert.Equal("NestedSetIsNonLeaf", flag.GetColumnName(StoreObjectIdentifier.Table("SharedNodes")));
        Assert.Null(payload.FindProperty("NestedSetIsNonLeaf"));
        Assert.Single(
            model
                .GetRelationalModel()
                .FindTable("SharedNodes", null)!.Columns,
            column => column.Name == "NestedSetIsNonLeaf");
    }

    /// <summary>
    /// An independent TPT owner shares inherited bounds rather than generating against its payload table.
    /// </summary>
    /// <param name="lateTable">Whether the derived table is selected after hierarchy configuration.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FindProperty_IndependentlyConfiguredTptOwner_KeepsFlagWithInheritedBounds(
        bool lateTable
    )
    {
        // Arrange
        using var context = new AncestorContext(
            "Sqlite",
            model =>
            {
                model
                    .Entity<AncestorNode>()
                    .HasKey(node => node.Id);

                model
                    .Entity<AncestorNode>()
                    .ToTable("InheritedStructure");

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
        var entity = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(DerivedAncestorNode))!;

        var flag = entity.FindProperty("NestedSetIsNonLeaf")!;
        var store = StoreObjectIdentifier.Table("InheritedStructure");

        // Assert
        Assert.Equal("NestedSetIsNonLeaf", flag.GetColumnName(store));
        Assert.Equal(
            "\"Right\" > CASE WHEN \"Left\" = 9223372036854775807 THEN \"Left\" ELSE \"Left\" + 1 END",
            flag.GetComputedColumnSql(store));

        Assert.Contains(
            entity
                .Model
                .GetRelationalModel()
                .FindTable("InheritedStructure", null)!.Columns,
            column => column.Name == "NestedSetIsNonLeaf");
    }
}

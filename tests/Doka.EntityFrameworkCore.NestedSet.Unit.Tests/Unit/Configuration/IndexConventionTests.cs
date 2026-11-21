namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies index reconciliation without depending on a database connection or migration adapter.</summary>
public sealed partial class IndexConventionTests
{
    /// <summary>A repeated scope mapping removes indexes owned solely by the old configuration.</summary>
    [Fact]
    public void HasNestedSet_RepeatedMapping_RemovesObsoleteOwnedIndexes()
    {
        // Arrange
        var model = new ModelBuilder();
        var entity = model.Entity<IndexNode>();
        entity.HasKey(node => node.Key);
        Configure(entity);

        // Act
        entity.HasNestedSet(builder => builder.HasScope(node => node.OtherScope));

        // Assert
        var indexes = entity
            .Metadata
            .GetIndexes()
            .ToArray();

        Assert.Equal(3, indexes.Length);
        Assert.All(indexes, index => Assert.Equal(nameof(IndexNode.OtherScope), index.Properties[0].Name));
    }

    /// <summary>An existing ordinary named index already supplies the requested access path.</summary>
    [Fact]
    public void Configure_ExistingNamedIndex_IsReused()
    {
        // Arrange
        var model = new ModelBuilder();
        var entity = model.Entity<IndexNode>();
        entity.HasKey(node => node.Key);
        var existing = entity
            .HasIndex(
                node => new
                {
                    node.Scope,
                    node.TreeId,
                    node.Left,
                },
                "CustomLeft")
            .Metadata;

        // Act
        Configure(entity);

        // Assert
        var index = Assert.Single(
            entity.Metadata.GetIndexes(),
            candidate => candidate.Properties[2].Name == nameof(IndexNode.Left));

        Assert.Same(existing, index);
        Assert.Equal(
            3,
            entity
                .Metadata
                .GetIndexes()
                .Count());
    }

    /// <summary>Repeated structural configuration is idempotent even when all roles are supplied again.</summary>
    [Fact]
    public void Configure_RepeatedMapping_DoesNotDuplicateIndexes()
    {
        // Arrange
        var model = new ModelBuilder();
        var entity = model.Entity<IndexNode>();
        entity.HasKey(node => node.Key);
        Configure(entity);
        var original = entity
            .Metadata
            .GetIndexes()
            .ToArray();

        // Act
        Configure(entity);

        // Assert
        Assert.Equal(original, entity.Metadata.GetIndexes());
    }

    /// <summary>Application ownership protects an old access path when the structural scope changes.</summary>
    /// <param name="facet">The user configuration that adopts or customizes the initially owned index.</param>
    [Theory]
    [InlineData("name")]
    [InlineData("unique")]
    [InlineData("nonunique")]
    [InlineData("descending")]
    [InlineData("ascending")]
    [InlineData("filter")]
    [InlineData("annotation")]
    [InlineData("adopt")]
    public void HasNestedSet_CustomizedOwnedIndex_SurvivesRemapping(
        string facet
    )
    {
        // Arrange
        var model = new ModelBuilder();
        var entity = model.Entity<IndexNode>();
        entity.HasKey(node => node.Key);
        Configure(entity);
        var customized = entity.HasIndex(node => new
        {
            node.Scope,
            node.TreeId,
            node.Left,
        });

        Customize(customized, facet);
        var annotations = customized
            .Metadata
            .GetAnnotations()
            .ToArray();

        var unique = customized.Metadata.IsUnique;
        var descending = customized.Metadata.IsDescending;

        // Act
        entity.HasNestedSet(builder => builder.HasScope(node => node.OtherScope));

        // Assert
        Assert.Contains(customized.Metadata, entity.Metadata.GetIndexes());
        Assert.Equal(
            4,
            entity
                .Metadata
                .GetIndexes()
                .Count());

        Assert.Equal(annotations, customized.Metadata.GetAnnotations());
        Assert.Equal(unique, customized.Metadata.IsUnique);
        Assert.Equal(descending, customized.Metadata.IsDescending);
    }

    /// <summary>Equivalent application indexes added after mapping replace only untouched library indexes.</summary>
    [Fact]
    public void FinalizeModel_LaterNamedIndex_ReplacesUnmodifiedOwnedIndex()
    {
        // Arrange
        using var context = new ConventionContext(
            "Sqlite",
            model =>
            {
                var entity = model.Entity<IndexNode>();
                entity.HasKey(node => node.Key);
                Configure(entity);
                entity.HasIndex(
                    node => new
                    {
                        node.Scope,
                        node.TreeId,
                        node.Left,
                    },
                    "UserLeft");
            });

        // Act
        var entity = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(IndexNode))!;

        // Assert
        var left = Assert.Single(
            entity.GetIndexes(),
            index => index.Properties.Count > 2 && index.Properties[2].Name == nameof(IndexNode.Left));

        Assert.Equal("UserLeft", left.Name);
        Assert.Equal(
            4,
            entity
                .GetIndexes()
                .Count());
    }

    /// <summary>A user-created index remains even if a repeated mapping no longer needs its properties.</summary>
    [Fact]
    public void HasNestedSet_ExistingUserIndex_SurvivesRemapping()
    {
        // Arrange
        var model = new ModelBuilder();
        var entity = model.Entity<IndexNode>();
        entity.HasKey(node => node.Key);
        var existing = entity
            .HasIndex(node => new
            {
                node.Scope,
                node.TreeId,
                node.Left,
            })
            .Metadata;

        Configure(entity);

        // Act
        entity.HasNestedSet(builder => builder.HasScope(node => node.OtherScope));

        // Assert
        Assert.Contains(existing, entity.Metadata.GetIndexes());
        Assert.Equal(
            4,
            entity
                .Metadata
                .GetIndexes()
                .Count());
    }

    /// <summary>Applies deliberate application changes for independent ownership retention checks.</summary>
    private static void Customize(
        IndexBuilder<IndexNode> index,
        string facet
    )
    {
        switch (facet)
        {
            case "name":
                index.HasDatabaseName("UserLeft");
                break;
            case "unique":
                index.IsUnique();
                break;
            case "nonunique":
                index.IsUnique(false);
                break;
            case "descending":
                index.IsDescending();
                break;
            case "ascending":
                index.IsDescending(false, false, false);
                break;
            case "filter":
                index.HasFilter("\"Left\" > 0");
                break;
            case "annotation":
                index.HasAnnotation("Application:SpecializedIndex", true);
                break;
            case "adopt":
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(facet));
        }
    }

    /// <summary>Applies explicit structural selectors while leaving EF primary-key ordering to each test.</summary>
    private static void Configure(
        EntityTypeBuilder<IndexNode> entity
    ) => entity.HasNestedSet(builder => builder
        .HasScope(node => node.Scope)
        .HasTreeId(node => node.TreeId)
        .HasParent(node => node.Parent)
        .HasBounds(node => node.Left, node => node.Right)
        .HasDepth(node => node.Depth)
        .HasPosition(node => node.Position));

    /// <summary>Provides alternate key and scope properties for model-ordering regressions.</summary>
    private sealed class IndexNode
    {
        /// <summary>Gets or sets the explicitly selected primary key.</summary>
        public int Key { get; set; }

        /// <summary>Gets or sets an alternative primary key.</summary>
        public int OtherKey { get; set; }

        /// <summary>Gets or sets the first forest scope.</summary>
        public int Scope { get; set; }

        /// <summary>Gets or sets the alternative forest scope.</summary>
        public int OtherScope { get; set; }

        /// <summary>Gets or sets the tree identity within either configured scope.</summary>
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the optional parent key.</summary>
        public int? Parent { get; set; }

        /// <summary>Gets or sets the left coordinate.</summary>
        public long Left { get; set; }

        /// <summary>Gets or sets the right coordinate.</summary>
        public long Right { get; set; }

        /// <summary>Gets or sets the persisted depth.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets the sibling position.</summary>
        public long Position { get; set; }
    }
}

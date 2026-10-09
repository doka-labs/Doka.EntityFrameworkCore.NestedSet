namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies index reconciliation without depending on a database connection or migration adapter.</summary>
public sealed partial class IndexConventionTests
{
    /// <summary>A repeated scope mapping removes indexes owned solely by the old configuration.</summary>
    [Fact]
    public void RepeatedMappingRemovesObsoleteOwnedIndexes()
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
    public void ExistingNamedIndexIsReused()
    {
        // Arrange
        var model = new ModelBuilder();
        var entity = model.Entity<IndexNode>();
        entity.HasKey(node => node.Key);
        var existing = entity.HasIndex(
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
    public void RepeatedMappingDoesNotDuplicateIndexes()
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
    public void CustomizedOwnedIndexSurvivesRemapping(
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
            node.Left
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
    public void LaterNamedIndexReplacesUnmodifiedOwnedIndex()
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
                        node.Left
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
    public void ExistingUserIndexSurvivesRemapping()
    {
        // Arrange
        var model = new ModelBuilder();
        var entity = model.Entity<IndexNode>();
        entity.HasKey(node => node.Key);
        var existing = entity.HasIndex(node => new{node.Scope, node.TreeId, node.Left}).Metadata;
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

    /// <summary>A late scalar primary key completes the three structural tree-local indexes.</summary>
    /// <param name="engine">The provider whose normal model pipeline is exercised.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    public void LatePrimaryKeyCompletesStructuralIndexes(
        string engine
    )
    {
        // Arrange
        using var context = new ConventionContext(
            engine,
            model =>
            {
                var entity = model.Entity<IndexNode>();
                Configure(entity);
                entity.HasKey(node => node.Key);
            });

        // Act
        var entity = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(IndexNode))!;

        // Assert
        Assert.Equal(
            engine == "PostgreSql" ? 5 : 4,
            entity
                .GetIndexes()
                .Count());
        Assert.All(
            entity
                .GetIndexes()
                .Where(index => index.Properties.Count > 2
                    && index.GetFilter()?.EndsWith(" IS NULL", StringComparison.Ordinal) != true),
            index => Assert.Contains(index.Properties, property => property.Name == nameof(IndexNode.TreeId)));
        Assert.All(entity.GetIndexes(), index => Assert.False(index.IsUnique));
    }

    /// <summary>An EF key replacement cannot silently invalidate the stable inferred NodeKey.</summary>
    /// <param name="engine">The provider whose normal model pipeline is exercised.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    public void ReplacedPrimaryKeyRejectsOrphanedNodeKey(
        string engine
    )
    {
        // Arrange
        using var context = new ConventionContext(
            engine,
            model =>
            {
                var entity = model.Entity<IndexNode>();
                entity.HasKey(node => node.Key);
                Configure(entity);
                entity.HasKey(node => node.OtherKey);
            });

        // Act
        var exception = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsAssignableFrom<InvalidOperationException>(exception);
        Assert.Contains("primary or alternate key", invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>Finalization rejects an explicit nested-set key that disagrees with the eventual EF key.</summary>
    [Fact]
    public void ExplicitKeyMismatchIsRejectedAtFinalization()
    {
        // Arrange
        using var context = new ConventionContext(
            "Sqlite",
            model =>
            {
                var entity = model.Entity<IndexNode>();
                Configure(entity);
                entity.HasNestedSet(builder => builder.HasNodeKey(node => node.Key));
                entity.HasKey(node => node.OtherKey);
            });

        // Act
        var exception = Record.Exception(() => context.Model);

        // Assert
        var mismatch = Assert.IsAssignableFrom<InvalidOperationException>(exception);
        Assert.Contains("primary or alternate key", mismatch.Message, StringComparison.Ordinal);
    }

    /// <summary>A scope-qualified composite key is a valid stable NodeKey identity.</summary>
    [Fact]
    public void CompositePrimaryKeyContainingScopeAndNodeKeyIsAccepted()
    {
        // Arrange
        using var context = new ConventionContext(
            "Sqlite",
            model =>
            {
                var entity = model.Entity<IndexNode>();
                Configure(entity);
                entity.HasNestedSet(builder => builder.HasNodeKey(node => node.Key));
                entity.HasKey(node => new
                {
                    node.Scope,
                    node.Key
                });
            });

        // Act
        var entity = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(IndexNode))!;

        // Assert
        Assert.Equal(2, entity.FindPrimaryKey()!.Properties.Count);
        Assert.Equal(
            4,
            entity
                .GetIndexes()
                .Count());
    }

    /// <summary>A specialized unnamed index cannot suppress the ordinary path needed by structural queries.</summary>
    /// <param name="facet">The incompatible index facet to preserve.</param>
    [Theory]
    [InlineData("unique")]
    [InlineData("descending")]
    [InlineData("filter")]
    [InlineData("annotation")]
    public void SpecializedIndexIsPreservedBesideOrdinaryAccessPath(
        string facet
    )
    {
        // Arrange
        using var context = new ConventionContext(
            "Sqlite",
            model =>
            {
                var entity = model.Entity<IndexNode>();
                entity.HasKey(node => node.Key);
                var special = entity.HasIndex(node => new
                {
                    node.Scope,
                    node.TreeId,
                    node.Left
                });
                Customize(special, facet);
                Configure(entity);
            });

        // Act
        var entity = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(IndexNode))!;

        // Assert
        var indexes = entity
            .GetIndexes()
            .Where(index => index.Properties.Count > 2 && index.Properties[2].Name == nameof(IndexNode.Left))
            .ToArray();

        Assert.Equal(2, indexes.Length);
        Assert.Equal(
            5,
            entity
                .GetIndexes()
                .Count());
        var original = Assert.Single(indexes, index => index.Name is null);
        var ordinary = Assert.Single(indexes, index => index.Name is not null);
        Assert.False(ordinary.IsUnique);
        Assert.Null(ordinary.IsDescending);
        Assert.All(ordinary.GetAnnotations(), annotation => Assert.Equal("Relational:Name", annotation.Name));
        Assert.True(
            original.IsUnique
            || original.IsDescending is not null
            || original
                .GetAnnotations()
                .Any());
    }

    /// <summary>Provider conventions still truncate generated names for long column and table names.</summary>
    /// <param name="engine">The provider whose identifier limit applies.</param>
    /// <param name="limit">The provider's maximum identifier length.</param>
    [Theory]
    [InlineData("MySql", 64)]
    [InlineData("MariaDb", 64)]
    [InlineData("PostgreSql", 63)]
    public void ProviderIdentifierLimitsArePreserved(
        string engine,
        int limit
    )
    {
        // Arrange
        using var context = new ConventionContext(
            engine,
            model =>
            {
                var entity = model.Entity<IndexNode>();
                entity.HasKey(node => node.Key);
                entity.ToTable("FolderHierarchyWithAnIntentionallyLongTableNameForIndexTests");
                entity
                    .Property(node => node.Scope)
                    .HasColumnName("TenantScopeWithALongDatabaseColumnName");
                entity
                    .Property(node => node.Key)
                    .HasColumnName("NodePrimaryKeyWithALongDatabaseColumnName");
                Configure(entity);
            });

        // Act
        var entity = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(IndexNode))!;

        // Assert
        Assert.Equal(
            engine == "PostgreSql" ? 5 : 4,
            entity
                .GetIndexes()
                .Count());

        var names = entity
            .GetIndexes()
            .Select(index => index.GetDatabaseName()!)
            .ToArray();
        Assert.All(names, name => Assert.InRange(name.Length, 1, limit));
        Assert.Equal(
            engine == "PostgreSql" ? 5 : 4,
            names
                .Distinct(StringComparer.Ordinal)
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

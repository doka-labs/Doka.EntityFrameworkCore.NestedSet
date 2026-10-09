using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class IndexConventionTests
{
    /// <summary>Nullable parent indexes partition roots and dependents without covering principal lookups.</summary>
    /// <param name="scoped">Whether the parent identity includes the configured scope.</param>
    /// <param name="renamed">Whether the final parent column requires provider-specific delimiting.</param>
    /// <param name="convertedTree">Whether a Guid tree identity is converted to text storage.</param>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void PostgreSqlNullableParentIndexesPartitionRows(
        bool scoped,
        bool renamed,
        bool convertedTree
    )
    {
        // Arrange
        using var context = new ConventionContext(
            "PostgreSql",
            model =>
            {
                var entity = ConfigureParentIndexNode(model, scoped);

                if (renamed)
                {
                    entity
                        .Property(node => node.Parent)
                        .HasColumnName("Parent \" key");
                    entity
                        .Property(node => node.TreeId)
                        .HasColumnName("Tree \" identity");
                }

                if (convertedTree)
                {
                    entity
                        .Property(node => node.TreeId)
                        .HasConversion<string>();
                }
            });

        // Act
        var model = context.GetService<IDesignTimeModel>().Model;
        var entity = model.FindEntityType(typeof(IndexNode))!;
        var indexes = entity
            .GetIndexes()
            .ToArray();

        var operations = context
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(null, model.GetRelationalModel())
            .OfType<CreateIndexOperation>()
            .Where(operation => operation.Table == entity.GetTableName())
            .ToArray();

        var script = context.Database.GenerateCreateScript();

        // Assert
        var parent = entity.FindProperty(nameof(IndexNode.Parent))!;
        var sql = context.GetService<ISqlGenerationHelper>();
        var column = sql.DelimitIdentifier(parent.GetColumnName());
        var dependent = Assert.Single(indexes, index => index.GetFilter() == column + " IS NOT NULL");
        var roots = Assert.Single(indexes, index => index.GetFilter() == column + " IS NULL");
        Assert.Equal(column + " IS NOT NULL", dependent.GetFilter());
        Assert.Equal(
            scoped ? [nameof(IndexNode.Scope), nameof(IndexNode.Parent)] : [nameof(IndexNode.Parent)],
            dependent.Properties.Select(property => property.Name));
        Assert.Equal(
            scoped
                ? [nameof(IndexNode.Scope), nameof(IndexNode.Key), nameof(IndexNode.Parent)]
                : [nameof(IndexNode.Key), nameof(IndexNode.Parent)],
            roots.Properties.Select(property => property.Name));
        Assert.False(dependent.IsUnique);
        Assert.False(roots.IsUnique);
        Assert.Equal(5, indexes.Length);
        Assert.Equal(5, operations.Count(operation => operation.Filter is not null));
        Assert.Contains(operations, operation => operation.Filter == dependent.GetFilter());
        Assert.Contains(operations, operation => operation.Filter == roots.GetFilter());
        Assert.Contains("WHERE " + dependent.GetFilter(), script, StringComparison.Ordinal);
        Assert.Contains("WHERE " + roots.GetFilter(), script, StringComparison.Ordinal);

        var tree = entity.FindProperty(nameof(IndexNode.TreeId))!;
        var treeFilter = sql.DelimitIdentifier(tree.GetColumnName()) + " IS NOT NULL";
        Assert.All(
            indexes.Where(index => index.Properties.Contains(tree)),
            index => Assert.Equal(treeFilter, index.GetFilter()));
        Assert.Equal(3, operations.Count(operation => operation.Filter == treeFilter));
    }

    /// <summary>Application-created or adopted parent indexes retain their complete configured metadata.</summary>
    /// <param name="facet">The explicit index setting that the convention must preserve.</param>
    [Theory]
    [InlineData("unnamed")]
    [InlineData("named")]
    [InlineData("filter")]
    [InlineData("unfiltered")]
    [InlineData("method")]
    [InlineData("annotation-filter")]
    [InlineData("database-name")]
    [InlineData("unique")]
    [InlineData("descending")]
    [InlineData("ascending")]
    public void PostgreSqlApplicationParentIndexesArePreserved(
        string facet
    )
    {
        // Arrange
        using var context = new ConventionContext(
            "PostgreSql",
            model =>
            {
                var entity = ConfigureParentIndexNode(model, true);

                if (facet == "annotation-filter")
                {
                    entity
                        .HasOne<IndexNode>()
                        .WithMany()
                        .HasForeignKey(node => new
                        {
                            node.Scope,
                            node.Parent,
                        })
                        .HasPrincipalKey(node => new
                        {
                            node.Scope,
                            node.Key,
                        })
                        .OnDelete(DeleteBehavior.Restrict);
                    ((IConventionIndex)entity.Metadata.FindIndex(
                    [
                        entity.Property(node => node.Scope).Metadata,
                        entity.Property(node => node.Parent).Metadata,
                    ])!).SetFilter(null, true);

                    return;
                }

                var index = facet == "named"
                    ? entity.HasIndex(
                        node => new
                        {
                            node.Scope,
                            node.Parent,
                        },
                        "ApplicationParent")
                    : entity.HasIndex(node => new
                    {
                        node.Scope,
                        node.Parent,
                    });

                switch (facet)
                {
                    case "filter":
                        index.HasFilter("\"Parent\" > 0");
                        break;
                    case "unfiltered":
                        index.HasFilter(null);
                        break;
                    case "method":
                        index.HasMethod("hash");
                        break;
                    case "database-name":
                        index.HasDatabaseName("ApplicationParentDatabaseName");
                        break;
                    case "unique":
                        index.IsUnique();
                        break;
                    case "descending":
                        index.IsDescending(true, true);
                        break;
                    case "ascending":
                        index.IsDescending(false, false);
                        break;
                }
            });

        // Act
        var entity = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(IndexNode))!;
        var indexes = entity
            .GetIndexes()
            .ToArray();

        // Assert
        var dependent = Assert.Single(indexes, index => index.Properties[^1].Name == nameof(IndexNode.Parent));
        Assert.Equal(facet == "filter" ? "\"Parent\" > 0" : null, dependent.GetFilter());
        Assert.Equal(facet == "named" ? "ApplicationParent" : null, dependent.Name);
        Assert.Equal(facet == "unique", dependent.IsUnique);
        Assert.Equal(4, indexes.Length);
        Assert.DoesNotContain(
            indexes,
            index => index
                    .GetFilter()
                    ?.EndsWith("IS NULL", StringComparison.Ordinal)
                == true);

        if (facet == "method")
        {
            Assert.Equal("hash", dependent["Npgsql:IndexMethod"]);
        }

        if (facet == "database-name")
        {
            Assert.Equal("ApplicationParentDatabaseName", dependent.GetDatabaseName());
        }

        if (facet is "descending" or "ascending")
        {
            Assert.Equal(facet == "descending" ? [] : null, dependent.IsDescending);
        }
    }

    /// <summary>Required relationships outside a configured hierarchy retain their ordinary dependent index.</summary>
    [Fact]
    public void PostgreSqlUnconfiguredParentIndexIsUnfiltered()
    {
        // Arrange
        using var context = new ConventionContext(
            "PostgreSql",
            model =>
            {
                var entity = model.Entity<IndexNode>();
                entity.HasKey(node => node.Key);
                entity
                    .Property(node => node.Parent)
                    .IsRequired();
                entity
                    .HasOne<IndexNode>()
                    .WithMany()
                    .HasForeignKey(node => node.Parent)
                    .OnDelete(DeleteBehavior.Restrict);
            });

        // Act
        var entity = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(IndexNode))!;

        // Assert
        Assert.Single(entity.GetIndexes());
        Assert.All(entity.GetIndexes(), index => Assert.Null(index.GetFilter()));
    }

    /// <summary>Other providers retain their existing unfiltered parent and structural indexes.</summary>
    /// <param name="engine">The supported provider whose index metadata must remain unchanged.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    public void OtherProviderParentIndexesAreUnfiltered(
        string engine
    )
    {
        // Arrange
        using var context = new ConventionContext(engine, model => ConfigureParentIndexNode(model, true));

        // Act
        var entity = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(IndexNode))!;

        // Assert
        Assert.Equal(4, entity.GetIndexes().Count());
        Assert.All(entity.GetIndexes(), index => Assert.Null(index.GetFilter()));
    }

    /// <summary>Root index names follow physical mappings when the hierarchy CLR and model names change.</summary>
    [Fact]
    public void PostgreSqlRootIndexNameIgnoresClrIdentity()
    {
        // Arrange
        using var original = new ConventionContext(
            "PostgreSql",
            model => ConfigureParentIndexNode(model, true)
                .ToTable("PhysicalRoots", "maps"));

        using var renamed = new ConventionContext(
            "PostgreSql",
            model =>
            {
                var entity = model.Entity<PhysicalAliasNode>();
                entity.ToTable("PhysicalRoots", "maps");
                entity.HasKey(node => node.Key);
                entity.HasNestedSet(builder => builder
                    .HasScope(node => node.Scope)
                    .HasTreeId(node => node.TreeId)
                    .HasParent(node => node.Parent)
                    .HasBounds(node => node.Left, node => node.Right)
                    .HasDepth(node => node.Depth)
                    .HasPosition(node => node.Position));
            });

        // Act
        var originalName = RootIndexName(original, typeof(IndexNode));
        var renamedName = RootIndexName(renamed, typeof(PhysicalAliasNode));

        // Assert
        Assert.Equal(originalName, renamedName);
    }

    /// <summary>Application structural indexes retain their facets alongside any generated tree fallback.</summary>
    /// <param name="facet">The application facet that must remain unchanged.</param>
    [Theory]
    [InlineData("unnamed")]
    [InlineData("named")]
    [InlineData("unfiltered")]
    [InlineData("filter")]
    [InlineData("unique")]
    [InlineData("descending")]
    [InlineData("method")]
    public void PostgreSqlApplicationTreeIndexesArePreserved(
        string facet
    )
    {
        // Arrange
        using var context = new ConventionContext(
            "PostgreSql",
            model =>
            {
                var entity = ConfigureParentIndexNode(model, true);
                var index = facet == "named"
                    ? entity.HasIndex(
                        node => new
                        {
                            node.Scope,
                            node.TreeId,
                            node.Left,
                        },
                        "ApplicationTree")
                    : entity.HasIndex(node => new
                    {
                        node.Scope,
                        node.TreeId,
                        node.Left,
                    });

                index.HasDatabaseName("ApplicationTreeDatabaseName");

                switch (facet)
                {
                    case "unfiltered":
                        index.HasFilter(null);
                        break;
                    case "filter":
                        index.HasFilter("\"Left\" > 0");
                        break;
                    case "unique":
                        index.IsUnique();
                        break;
                    case "descending":
                        index.IsDescending();
                        break;
                    case "method":
                        index.HasMethod("hash");
                        break;
                }
            });

        // Act
        var entity = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(IndexNode))!;

        var application = entity
            .GetIndexes()
            .Single(index => index.GetDatabaseName() == "ApplicationTreeDatabaseName");

        // Assert
        Assert.Equal(facet == "named" ? "ApplicationTree" : null, application.Name);
        Assert.Equal(facet == "filter" ? "\"Left\" > 0" : null, application.GetFilter());
        Assert.Equal(facet == "unique", application.IsUnique);
        Assert.Equal(facet == "descending" ? [] : null, application.IsDescending);
        Assert.Equal(facet == "method" ? "hash" : null, application["Npgsql:IndexMethod"]);
        Assert.Equal(
            facet is "filter" or "unique" or "descending" or "method" ? 6 : 5,
            entity
                .GetIndexes()
                .Count());
    }

    /// <summary>An application database-name collision retains its index and gives roots a distinct name.</summary>
    [Fact]
    public void PostgreSqlRootIndexNamePreservesApplicationDatabaseName()
    {
        // Arrange
        using var baseline = new ConventionContext("PostgreSql", model => ConfigureParentIndexNode(model, true));
        var originalName = RootIndexName(baseline, typeof(IndexNode));
        using var context = new ConventionContext(
            "PostgreSql",
            model =>
            {
                var entity = ConfigureParentIndexNode(model, true);
                entity
                    .HasIndex(node => node.OtherKey)
                    .HasDatabaseName(originalName);
            });

        // Act
        var entity = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(IndexNode))!;

        var rootName = RootIndexName(context, typeof(IndexNode));

        // Assert
        var application = Assert.Single(
            entity.GetIndexes(),
            index => index.Properties is [{ Name: nameof(IndexNode.OtherKey) }]);

        Assert.Equal(originalName, application.GetDatabaseName());
        Assert.Null(application.GetFilter());
        Assert.Equal(originalName + "_1", rootName);
        Assert.Equal(6, entity.GetIndexes().Count());
    }

    /// <summary>Gets the root companion's physical name from the completed provider model.</summary>
    private static string RootIndexName(
        ConventionContext context,
        Type type
    ) => context
        .GetService<IDesignTimeModel>()
        .Model
        .FindEntityType(type)!
        .GetIndexes()
        .Single(index => index.GetFilter()?.EndsWith("IS NULL", StringComparison.Ordinal) == true)
        .GetDatabaseName()!;

    /// <summary>Uses the same physical structure through an independently named CLR entity.</summary>
    private sealed class PhysicalAliasNode
    {
        /// <summary>Gets or sets the node key.</summary>
        public int Key { get; set; }

        /// <summary>Gets or sets the hierarchy scope.</summary>
        public int Scope { get; set; }

        /// <summary>Gets or sets the tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the optional parent key.</summary>
        public int? Parent { get; set; }

        /// <summary>Gets or sets the left coordinate.</summary>
        public long Left { get; set; }

        /// <summary>Gets or sets the right coordinate.</summary>
        public long Right { get; set; }

        /// <summary>Gets or sets the depth.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets the sibling position.</summary>
        public long Position { get; set; }
    }

    /// <summary>Builds otherwise identical scoped and unscoped parent relationships.</summary>
    private static EntityTypeBuilder<IndexNode> ConfigureParentIndexNode(
        ModelBuilder model,
        bool scoped
    )
    {
        var entity = model.Entity<IndexNode>();
        entity.HasKey(node => node.Key);
        entity.HasNestedSet(builder =>
        {
            builder
                .HasTreeId(node => node.TreeId)
                .HasParent(node => node.Parent)
                .HasBounds(node => node.Left, node => node.Right)
                .HasDepth(node => node.Depth)
                .HasPosition(node => node.Position);

            if (scoped)
            {
                builder.HasScope(node => node.Scope);
            }
        });

        return entity;
    }
}

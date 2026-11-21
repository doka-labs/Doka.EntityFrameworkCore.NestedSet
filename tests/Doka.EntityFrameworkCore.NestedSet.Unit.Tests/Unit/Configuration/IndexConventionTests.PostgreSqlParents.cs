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
    public void GetIndexes_PostgreSqlNullableParentIndexes_PartitionRows(
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
        var model = context.GetService<IDesignTimeModel>()
            .Model;

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
            scoped
                ? [nameof(IndexNode.Scope), nameof(IndexNode.Parent)]
                : [nameof(IndexNode.Parent)],
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

    /// <summary>Required relationships outside a configured hierarchy retain their ordinary dependent index.</summary>
    [Fact]
    public void FinalizeModel_PostgreSqlUnconfiguredParentIndex_IsUnfiltered()
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
    public void FinalizeModel_OtherProviderParentIndexes_AreUnfiltered(
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
        Assert.Equal(
            4,
            entity
                .GetIndexes()
                .Count());

        Assert.All(entity.GetIndexes(), index => Assert.Null(index.GetFilter()));
    }

    /// <summary>Root index names follow physical mappings when the hierarchy CLR and model names change.</summary>
    [Fact]
    public void RootIndexName_PostgreSqlRootIndexName_IgnoresClrIdentity()
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

    /// <summary>Gets the root companion's physical name from the completed provider model.</summary>
    private static string RootIndexName(
        ConventionContext context,
        Type type
    ) => context
        .GetService<IDesignTimeModel>()
        .Model
        .FindEntityType(type)!
        .GetIndexes()
        .Single(index => index
                .GetFilter()
                ?.EndsWith("IS NULL", StringComparison.Ordinal)
            == true)
        .GetDatabaseName()!;

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
}

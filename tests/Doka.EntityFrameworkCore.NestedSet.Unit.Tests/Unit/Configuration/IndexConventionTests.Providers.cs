namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class IndexConventionTests
{
    /// <summary>Doka sees the late composite index while assigning implicit string and binary index lengths.</summary>
    /// <param name="engine">The Doka server family whose model conventions run.</param>
    /// <param name="binary">Whether keys and scopes use binary values instead of strings.</param>
    [Theory]
    [InlineData("MySql", false)]
    [InlineData("MariaDb", false)]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", true)]
    public void DokaBudgetsImplicitVariableLengthIndexProperties(
        string engine,
        bool binary
    )
    {
        // Arrange
        using var context = new ConventionContext(
            engine,
            model =>
            {
                if (binary)
                {
                    ConfigureVariableNode<byte[]>(model);
                }
                else
                {
                    ConfigureVariableNode<string>(model);
                }
            });

        // Act
        var type = binary ? typeof(VariableNode<byte[]>) : typeof(VariableNode<string>);
        var entity = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(type)!;

        var script = context.Database.GenerateCreateScript();

        // Assert
        Assert.Equal(
            4,
            entity
                .GetIndexes()
                .Count());
        Assert.All(
            entity
                .GetIndexes()
                .SelectMany(index => index.Properties)
                .Where(property => property.ClrType == typeof(string) || property.ClrType == typeof(byte[])),
            property => Assert.Equal(255, property.GetMaxLength()));
        Assert.All(
            entity
                .GetIndexes()
                .Where(index => index.Properties.Count > 2),
            index => Assert.Equal(nameof(VariableNode<string>.TreeId), index.Properties[1].Name));
        Assert.Contains(binary ? "varbinary(255)" : "varchar(255)", script, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>PostgreSQL index methods and comparison overrides are not mistaken for an ordinary B-tree.</summary>
    /// <param name="facet">The specialized public Npgsql option.</param>
    [Theory]
    [InlineData("hash")]
    [InlineData("gin")]
    [InlineData("operators")]
    [InlineData("collation")]
    public void SpecializedPostgreSqlIndexDoesNotReplaceOrdinaryAccessPath(
        string facet
    )
    {
        // Arrange
        using var context = new ConventionContext(
            "PostgreSql",
            model =>
            {
                var entity = model.Entity<IndexNode>();
                entity.HasKey(node => node.Key);
                var index = entity.HasIndex(
                    node => new
                    {
                        node.Scope,
                        node.TreeId,
                        node.Left,
                    },
                    "SpecializedLeft");

                switch (facet)
                {
                    case "hash":
                    case "gin":
                        index.HasMethod(facet);
                        break;
                    case "operators":
                        index.HasOperators("int4_ops", "uuid_ops", "int8_ops");
                        break;
                    case "collation":
                        index.UseCollation("C", "C", "C");
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(facet));
                }

                Configure(entity);
            });

        // Act
        var entity = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(IndexNode))!;

        // Assert
        var leftIndexes = entity
            .GetIndexes()
            .Where(index => index.Properties.Count > 2 && index.Properties[2].Name == nameof(IndexNode.Left))
            .ToArray();

        Assert.Equal(2, leftIndexes.Length);
        Assert.Equal(
            5,
            entity
                .GetIndexes()
                .Count());
        Assert.Contains(leftIndexes, index => index.Name == "SpecializedLeft");
        Assert.Contains(leftIndexes, index => index.Name is null);
    }

    /// <summary>Explicit variable lengths and collations are never overwritten by the index convention.</summary>
    /// <param name="engine">The real provider whose model pipeline supplies relational types.</param>
    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("Sqlite")]
    public void ExplicitStringPropertyFacetsArePreserved(
        string engine
    )
    {
        // Arrange
        var collation = engine switch
        {
            "MySql" or "MariaDb" => "utf8mb4_bin", "PostgreSql" => "C", _ => "BINARY",
        };

        using var context = new ConventionContext(
            engine,
            model =>
            {
                var entity = model.Entity<VariableNode<string>>();
                entity
                    .Property(node => node.Key)
                    .HasMaxLength(96)
                    .UseCollation(collation);
                entity
                    .Property(node => node.Parent)
                    .HasMaxLength(96)
                    .UseCollation(collation);
                entity
                    .Property(node => node.Scope)
                    .HasMaxLength(48)
                    .UseCollation(collation);
                ConfigureVariableNode<string>(model);
            });

        // Act
        var entity = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(VariableNode<string>))!;

        // Assert
        Assert.Equal(96, entity.FindProperty(nameof(VariableNode<string>.Key))!.GetMaxLength());
        Assert.Equal(96, entity.FindProperty(nameof(VariableNode<string>.Parent))!.GetMaxLength());
        Assert.Equal(48, entity.FindProperty(nameof(VariableNode<string>.Scope))!.GetMaxLength());
        Assert.Equal(collation, entity.FindProperty(nameof(VariableNode<string>.Key))!.GetCollation());
        Assert.Equal(collation, entity.FindProperty(nameof(VariableNode<string>.Parent))!.GetCollation());
        Assert.Equal(collation, entity.FindProperty(nameof(VariableNode<string>.Scope))!.GetCollation());
    }

    /// <summary>Configures all structural roles before the variable-length primary key becomes known.</summary>
    private static void ConfigureVariableNode<TKey>(
        ModelBuilder model
    )
        where TKey : class
    {
        var entity = model.Entity<VariableNode<TKey>>();
        entity.HasNestedSet(builder => builder
            .HasScope(node => node.Scope)
            .HasTreeId(node => node.TreeId)
            .HasParent(node => node.Parent)
            .HasBounds(node => node.Left, node => node.Right)
            .HasDepth(node => node.Depth)
            .HasPosition(node => node.Position));
        entity.HasKey(node => node.Key);
    }

    /// <summary>Supplies variable-length keys and scopes without explicit type or length hints.</summary>
    /// <typeparam name="TKey">A string or byte-array key representation.</typeparam>
    private sealed class VariableNode<TKey>
        where TKey : class
    {
        /// <summary>Gets or sets the explicitly selected primary key.</summary>
        public TKey Key { get; set; } = null!;

        /// <summary>Gets or sets the forest scope.</summary>
        public TKey Scope { get; set; } = null!;

        /// <summary>Gets or sets the stable tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the optional parent key.</summary>
        public TKey? Parent { get; set; }

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

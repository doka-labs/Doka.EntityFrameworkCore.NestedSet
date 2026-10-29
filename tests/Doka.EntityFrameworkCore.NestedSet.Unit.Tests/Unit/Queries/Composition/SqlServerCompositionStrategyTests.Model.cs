using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class SqlServerCompositionStrategyTests
{
    /// <summary>Creates independent synthetic coordinates with explicit scope, tree and visibility controls.</summary>
    private static CoordinateBoundsNode BoundsNode(
        int id,
        long left,
        long right,
        int? parent = null,
        int depth = 0,
        bool visible = true,
        string tree = "a",
        int scope = 7
    ) => new()
    {
        Id = new BoundsKey(id), Scope = scope, TreeId = tree,
        ParentId = parent.HasValue ? new BoundsKey(parent.Value) : null,
        Left = left, Right = right, Depth = depth, Visible = visible,
    };

    /// <summary>Combines an operator-free converted key with database-native case-insensitive tree identity.</summary>
    private sealed class CoordinateBoundsContext(DbContextOptions<CoordinateBoundsContext> options, bool scoped = true)
        : DbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var node = modelBuilder.Entity<CoordinateBoundsNode>();
            var converter = new ValueConverter<BoundsKey, int>(key => key.Value, value => new BoundsKey(value));
            if (scoped)
            {
                node.HasKey(entity => new { entity.Scope, entity.Id });
            }
            else
            {
                node.HasKey(entity => entity.Id);
            }

            node.Property(entity => entity.Id).HasConversion(converter).ValueGeneratedNever();
            node.Property(entity => entity.ParentId).HasConversion(converter);
            node.Property(entity => entity.TreeId).HasMaxLength(40)
                .UseCollation(Database.IsSqlServer() ? "Latin1_General_100_BIN2" : "NOCASE");
            node.HasQueryFilter(entity => entity.Visible);
            node.HasNestedSet(hierarchy =>
            {
                hierarchy.HasNodeKey(entity => entity.Id)
                    .HasTreeId(entity => entity.TreeId)
                    .HasParent(entity => entity.ParentId)
                    .HasBounds(entity => entity.Left, entity => entity.Right)
                    .HasDepth(entity => entity.Depth)
                    .HasPosition(entity => entity.Position);

                if (scoped)
                {
                    hierarchy.HasScope(entity => entity.Scope);
                }
            });
        }
    }

    /// <summary>Provides a converted identity without a CLR equality operator.</summary>
    private readonly struct BoundsKey(int value)
    {
        /// <summary>Gets the synthetic scalar identity.</summary>
        public int Value { get; } = value;
    }

    /// <summary>Represents only the structural roles needed by adversarial key and path controls.</summary>
    private sealed class CoordinateBoundsNode
    {
        /// <summary>Gets or sets the independently converted node key.</summary>
        public BoundsKey Id { get; set; }

        /// <summary>Gets or sets the application partition.</summary>
        public int Scope { get; set; }

        /// <summary>Gets or sets the database-collated tree identity.</summary>
        public string TreeId { get; set; } = "a";

        /// <summary>Gets or sets the optional directly enclosing key.</summary>
        public BoundsKey? ParentId { get; set; }

        /// <summary>Gets or sets the opening bound.</summary>
        public long Left { get; set; }

        /// <summary>Gets or sets the closing bound.</summary>
        public long Right { get; set; }

        /// <summary>Gets or sets the structural depth.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets the sibling position.</summary>
        public long Position { get; set; }

        /// <summary>Gets or sets whether the application's visibility filter admits the owner.</summary>
        public bool Visible { get; set; } = true;
    }
}

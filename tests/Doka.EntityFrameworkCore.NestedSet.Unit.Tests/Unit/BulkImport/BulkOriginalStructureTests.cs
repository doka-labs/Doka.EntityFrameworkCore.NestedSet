namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects compact coordinate snapshots and their distinction from configured sentinels.</summary>
public sealed class BulkOriginalStructureTests
{
    /// <summary>Native coordinate snapshots retain zero and extreme values independently of a nonzero sentinel.</summary>
    /// <param name="boundary">The exact caller-owned boundary and sibling position.</param>
    /// <param name="depth">The exact caller-owned depth.</param>
    [Theory]
    [InlineData(0L, 0)]
    [InlineData(long.MinValue, int.MinValue)]
    [InlineData(long.MaxValue, int.MaxValue)]
    public async Task NonSentinelCoordinatesRetainTheirExactValues(
        long boundary,
        int depth
    )
    {
        // Arrange
        await using var context = CreateContext();
        var map = NestedSetMapping<TreeNode, int, int>.For(context, context.Model.FindEntityType(typeof(TreeNode))!);
        var input = new TreeNode
        {
            Start = boundary,
            End = boundary,
            Depth = depth,
            Position = boundary,
        };

        // Act
        var original = NestedSetBulkPlan<TreeNode, int, Guid, int>.OriginalStructure.Capture(input, map);

        // Assert
        Assert.NotNull(original);
        Assert.Equal(boundary, Assert.IsType<long>(original.LeftOr(map.LeftProperty.Sentinel)));
        Assert.Equal(boundary, Assert.IsType<long>(original.RightOr(map.RightProperty.Sentinel)));
        Assert.Equal(depth, Assert.IsType<int>(original.DepthOr(map.DepthProperty.Sentinel)));
        Assert.Equal(boundary, Assert.IsType<long>(original.PositionOr(map.PositionProperty.Sentinel)));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Inputs entirely described by metadata sentinels need no allocated rollback snapshot.</summary>
    [Fact]
    public async Task AllSentinelCoordinatesNeedNoSnapshot()
    {
        // Arrange
        await using var context = CreateContext();
        var map = NestedSetMapping<TreeNode, int, int>.For(context, context.Model.FindEntityType(typeof(TreeNode))!);
        var input = new TreeNode
        {
            Start = -1L,
            End = -1L,
            Depth = -1,
            Position = -1L,
        };

        // Act
        var original = NestedSetBulkPlan<TreeNode, int, Guid, int>.OriginalStructure.Capture(input, map);

        // Assert
        Assert.Null(original);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>A captured coordinate does not turn omitted coordinates into native default values.</summary>
    [Fact]
    public async Task SparseSnapshotUsesTheSentinelForOmittedCoordinates()
    {
        // Arrange
        await using var context = CreateContext();
        var map = NestedSetMapping<TreeNode, int, int>.For(context, context.Model.FindEntityType(typeof(TreeNode))!);
        var input = new TreeNode
        {
            Start = 0L,
            End = -1L,
            Depth = -1,
            Position = -1L,
        };

        // Act
        var original = NestedSetBulkPlan<TreeNode, int, Guid, int>.OriginalStructure.Capture(input, map);

        // Assert
        Assert.NotNull(original);
        Assert.Equal(0L, Assert.IsType<long>(original.LeftOr(map.LeftProperty.Sentinel)));
        Assert.Same(map.RightProperty.Sentinel, original.RightOr(map.RightProperty.Sentinel));
        Assert.Same(map.DepthProperty.Sentinel, original.DepthOr(map.DepthProperty.Sentinel));
        Assert.Same(map.PositionProperty.Sentinel, original.PositionOr(map.PositionProperty.Sentinel));
    }

    /// <summary>Creates finalized metadata with deliberately nondefault coordinate sentinels.</summary>
    private static SnapshotContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SnapshotContext>()
            .UseSqlite("Data Source=:memory:")
            .UseNestedSets()
            .Options;

        return new SnapshotContext(options);
    }

    /// <summary>Maps the supported native coordinate roles with distinct metadata sentinels.</summary>
    private sealed class SnapshotContext : DbContext
    {
        /// <summary>Uses a provider-backed model without requiring a database connection.</summary>
        internal SnapshotContext(
            DbContextOptions<SnapshotContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var node = modelBuilder.Entity<TreeNode>();
            node.HasKey(value => value.NodeId);
            node
                .Property(value => value.NodeId)
                .ValueGeneratedNever();
            node.HasNestedSet(value => value
                .HasNodeKey(entity => entity.NodeId)
                .HasBounds(entity => entity.Start, entity => entity.End)
                .HasDepth(entity => entity.Depth)
                .HasTreeId(entity => entity.TreeId)
                .HasScope(entity => entity.Tree)
                .HasParent(entity => entity.Parent)
                .HasPosition(entity => entity.Position));

            node
                .Property(value => value.Start)
                .HasSentinel(-1L);
            node
                .Property(value => value.End)
                .HasSentinel(-1L);
            node
                .Property(value => value.Depth)
                .HasSentinel(-1);
            node
                .Property(value => value.Position)
                .HasSentinel(-1L);
        }
    }
}

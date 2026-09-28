namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Keeps binary NodeKeys on the native scalar fallback without assuming CLR database equality.</summary>
public abstract partial class NativeTrackedIdentityGuardTests
{
    /// <summary>Binary NodeKeys fit one fixed-parameter membership probe beyond the former 64-key limit.</summary>
    [Fact]
    public async Task BinaryKeysUseOneNativeProbeForSixtyFiveCandidates()
    {
        // Arrange
        var probe = new NativeGuardProbe();
        await using var context = await _fixture.CreateContextAsync<BinaryGuardContext>(
            Engine,
            static options => new BinaryGuardContext(options),
            probe);

        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var affectedTree = Guid.NewGuid();
        var otherTree = Guid.NewGuid();
        await SeedBinaryTreeAsync(context, affectedTree, root, 1);
        await SeedBinaryTreeAsync(context, otherTree, root + 100, 64);
        var tracked = await context
            .Set<BinaryGuardNode>()
            .Where(node => node.TreeId == otherTree)
            .ToArrayAsync(CancellationToken.None);

        probe.Armed = true;

        // Act
        var failure = await Record.ExceptionAsync(() => context
            .NestedSet<BinaryGuardNode>()
            .DeleteTreeAsync(affectedTree, CancellationToken.None));

        // Assert
        Assert.Null(failure);
        Assert.Equal(65, tracked.Length);
        Assert.Equal(1, probe.NativeReads);

        // WHY: EF10 pads a 65-element fixed IN collection to 70 key parameters, plus one TreeId parameter.
        Assert.Equal(71, probe.MaximumNativeParameters);
        Assert.All(tracked, node => Assert.Equal(EntityState.Unchanged, context.Entry(node).State));
    }

    /// <summary>Imports fixed-width distinct binary keys without provider-dependent padding aliases.</summary>
    private static async Task SeedBinaryTreeAsync(
        DbContext context,
        Guid treeId,
        int root,
        int children
    )
    {
        var branches = Enumerable
            .Range(0, children)
            .Select(index => new NestedSetBranch<BinaryGuardNode>(
                new BinaryGuardNode
                {
                    Id = BitConverter.GetBytes(root + index + 1),
                    Name = $"Child-{index:D3}",
                }))
            .ToArray();

        var branch = new NestedSetBranch<BinaryGuardNode>(
            new BinaryGuardNode
            {
                Id = BitConverter.GetBytes(root),
                Name = "Root",
            },
            branches);

        await context
            .NestedSet<BinaryGuardNode>()
            .InsertForestAsync(
                [new NestedSetTreeImport<BinaryGuardNode, Guid>(treeId, branch)],
                CancellationToken.None);

        context.ChangeTracker.Clear();
    }

    /// <summary>Maps binary key and parent columns with identical fixed maximum widths.</summary>
    private sealed class BinaryGuardContext(DbContextOptions<BinaryGuardContext> options) : DbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var node = modelBuilder.Entity<BinaryGuardNode>();
            node.ToTable("BinaryGuardNodes");
            node.HasKey(value => value.Id);
            node
                .Property(value => value.Id)
                .HasMaxLength(sizeof(int))
                .ValueGeneratedNever();
            node
                .Property(value => value.ParentId)
                .HasMaxLength(sizeof(int));
            node
                .Property(value => value.Name)
                .HasMaxLength(100);
            node
                .HasOne<BinaryGuardNode>()
                .WithMany()
                .HasForeignKey(value => value.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
            node.HasNestedSet(nestedSet => nestedSet
                .HasNodeKey(value => value.Id)
                .HasTreeId(value => value.TreeId)
                .HasParent(value => value.ParentId)
                .HasBounds(value => value.Left, value => value.Right)
                .HasDepth(value => value.Depth)
                .HasPosition(value => value.Position));
        }
    }

    /// <summary>Provides mutable binary keys while retaining required typed geometry and tree identity.</summary>
    private sealed class BinaryGuardNode
    {
        /// <summary>Gets or sets the binary node key.</summary>
        public byte[] Id { get; set; } = [];

        /// <summary>Gets or sets the stable tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the optional binary parent key.</summary>
        public byte[]? ParentId { get; set; }

        /// <summary>Gets or sets the left traversal bound.</summary>
        public long Left { get; set; }

        /// <summary>Gets or sets the right traversal bound.</summary>
        public long Right { get; set; }

        /// <summary>Gets or sets the root-relative depth.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets the sibling position.</summary>
        public long Position { get; set; }

        /// <summary>Gets or sets the ordinary application payload.</summary>
        public string Name { get; set; } = string.Empty;
    }
}

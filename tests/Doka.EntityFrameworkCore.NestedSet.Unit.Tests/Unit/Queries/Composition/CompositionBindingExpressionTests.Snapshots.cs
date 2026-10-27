namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class CompositionBindingExpressionTests
{
    /// <summary>Fused bindings own independent binary values even when configured snapshots are shallow.</summary>
    [Fact]
    public async Task BinaryBindingSnapshotsRemainIndependentAndDeferred()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        var options = new DbContextOptionsBuilder<SnapshotBindingContext>()
            .UseSqlite(
                connection,
                provider => provider.UseNestedSets());
        await using var services = Services(options.Options);
        await using var context = new SnapshotBindingContext(options.UseInternalServiceProvider(services).Options);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        await context.Set<SnapshotBindingNode>().AddRangeAsync(
        [
            new SnapshotBindingNode { Id = 1, Scope = [1], Tree = [2], Left = 1, Right = 2 },
            new SnapshotBindingNode { Id = 2, Scope = [9], Tree = [2], Left = 1, Right = 2 },
            new SnapshotBindingNode { Id = 3, Scope = [1], Tree = [8], Left = 1, Right = 2 },
        ], CancellationToken.None);
        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        byte[] scope = [1];
        byte[] tree = [2];
        var original = context.NestedSet<SnapshotBindingNode>().ForScope(scope).InTree(tree);
        scope[0] = 9;
        tree[0] = 8;
        var changed = context.NestedSet<SnapshotBindingNode>().ForScope(scope).InTree(tree);

        // Act
        var originalRows = await original.Nodes.Select(node => node.Id).ToArrayAsync(CancellationToken.None);
        var changedRows = await changed.Nodes.Select(node => node.Id).ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([1], originalRows);
        Assert.Empty(changedRows);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Maps binary scope/tree identities with deliberately shallow application snapshots.</summary>
    private sealed class SnapshotBindingContext(DbContextOptions<SnapshotBindingContext> options) : DbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var node = modelBuilder.Entity<SnapshotBindingNode>();
            node.HasKey(value => value.Id);
            node.Property(value => value.Id).ValueGeneratedNever();

            foreach (var property in new[] { nameof(SnapshotBindingNode.Scope), nameof(SnapshotBindingNode.Tree) })
            {
                node.Property<byte[]>(property).HasMaxLength(8).Metadata.SetValueComparer(new ValueComparer<byte[]>(
                    (left, right) => left != null && right != null && left.SequenceEqual(right),
                    value => value.Aggregate(0, HashCode.Combine), value => value));
            }

            node.HasNestedSet(hierarchy => hierarchy.HasNodeKey(value => value.Id).HasScope(value => value.Scope)
                .HasTreeId(value => value.Tree).HasParent(value => value.ParentId)
                .HasBounds(value => value.Left, value => value.Right).HasDepth(value => value.Depth)
                .HasPosition(value => value.Position));
        }
    }

    /// <summary>Uses ordinary CLR geometry and binary identities without additional hierarchy semantics.</summary>
    private sealed class SnapshotBindingNode
    {
        /// <summary>Gets or sets the node identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the mutable application scope identity.</summary>
        public byte[] Scope { get; set; } = [];

        /// <summary>Gets or sets the mutable stable tree identity.</summary>
        public byte[] Tree { get; set; } = [];

        /// <summary>Gets or sets the nullable direct parent.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets the interval start.</summary>
        public long Left { get; set; }

        /// <summary>Gets or sets the interval end.</summary>
        public long Right { get; set; }

        /// <summary>Gets or sets the maintained depth.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets the maintained sibling position.</summary>
        public long Position { get; set; }
    }
}

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies linear guard capture and independent identity snapshots without database access.</summary>
public sealed class TrackedIdentityCaptureTests
{
    private static long s_providerConversions;

    /// <summary>Provider conversion work grows with candidates and requests rather than their product.</summary>
    [Fact]
    public void CaptureProviderWorkScalesLinearly()
    {
        // Arrange
        const int candidateCount = 20_000;
        const int requestCount = 65;
        using var context = new CountedContext();
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        var metadata = context.Model.FindEntityType(typeof(CountedNode))!;
        var map = NestedSetMapping<CountedNode, int, CountedIdentity>.For(context, metadata);
        var scope = new CountedIdentity(7);
        var requests = Enumerable
            .Range(1, requestCount)
            .Select(treeId => new NestedSetTreeLockRequest<CountedIdentity, CountedIdentity>(
                metadata,
                scope,
                new CountedIdentity(treeId),
                NestedSetTreeLockMode.Existing))
            .ToArray();

        context.AttachRange(
            Enumerable
                .Range(1, candidateCount)
                .Select(id => new CountedNode
                {
                    Id = id,
                    Scope = scope,
                    TreeId = new CountedIdentity(100_000 + id),
                    Left = 1,
                    Right = 2,
                }));

        // WHY: Model construction and attaching entries perform unrelated conversion work. Reset after setup;
        // this private converter is used only by this test class, whose cases xUnit runs sequentially.
        Interlocked.Exchange(ref s_providerConversions, 0);

        // Act
        using var guard =
            NestedSetTrackedIdentityGuard<CountedNode, int, CountedIdentity, CountedIdentity>.Capture(
                context,
                map,
                requests);

        guard?.VerifyUnchanged();
        var conversions = Interlocked.Read(ref s_providerConversions);

        // Assert
        Assert.NotNull(guard);
        Assert.True(guard.HasCandidates);
        Assert.InRange(conversions, 1L, 12L * (candidateCount + requestCount));
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            candidateCount,
            context
                .ChangeTracker
                .Entries<CountedNode>()
                .Count());
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Equivalent binary replacements are accepted through each captured typed property entry.</summary>
    /// <param name="role">The independently mutable identity being replaced.</param>
    [Theory]
    [InlineData("Key")]
    [InlineData("Scope")]
    [InlineData("TreeId")]
    public void VerifyAcceptsEquivalentBinaryReplacement(
        string role
    )
    {
        // Arrange
        using var context = new BinaryContext();
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        var node = new BinaryNode
        {
            Id = [1],
            Scope = [2],
            TreeId = [3],
            Left = 1,
            Right = 2,
        };

        var entry = context.Attach(node);
        using var guard = CaptureBinary(context);
        var replacement = Identity(node, role).ToArray();

        // Act
        ReplaceIdentity(node, role, replacement);
        var failure = Record.Exception(guard.VerifyUnchanged);

        // Assert
        Assert.Null(failure);
        Assert.Same(replacement, Identity(node, role));
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.Same(node, Assert.Single(context.ChangeTracker.Entries<BinaryNode>()).Entity);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>A shallow model snapshot cannot hide in-place edits to any captured binary identity.</summary>
    /// <param name="role">The identity whose array is changed after capture.</param>
    [Theory]
    [InlineData("Key")]
    [InlineData("Scope")]
    [InlineData("TreeId")]
    public void VerifyRejectsInPlaceBinaryIdentityChange(
        string role
    )
    {
        // Arrange
        using var context = new BinaryContext();
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        var node = new BinaryNode
        {
            Id = [1],
            Scope = [2],
            TreeId = [3],
            Left = 1,
            Right = 2,
        };

        var entry = context.Attach(node);
        using var guard = CaptureBinary(context);
        var identity = Identity(node, role);

        // Act
        identity[0] = 9;
        var failure = Record.Exception(guard.VerifyUnchanged);

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(failure).Code);
        Assert.Same(identity, Identity(node, role));
        Assert.Equal(9, identity[0]);
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.Same(node, Assert.Single(context.ChangeTracker.Entries<BinaryNode>()).Entity);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>The guard retains the original entry state even when cached property wrappers remain valid.</summary>
    [Fact]
    public void VerifyRejectsDetachedCapturedEntry()
    {
        // Arrange
        using var context = new BinaryContext();
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        var node = new BinaryNode
        {
            Id = [1],
            Scope = [2],
            TreeId = [3],
            Left = 1,
            Right = 2,
        };

        var entry = context.Attach(node);
        using var guard = CaptureBinary(context);

        // Act
        entry.State = EntityState.Detached;
        var failure = Record.Exception(guard.VerifyUnchanged);

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(failure).Code);
        Assert.Equal(EntityState.Detached, entry.State);
        Assert.Empty(context.ChangeTracker.Entries<BinaryNode>());
        Assert.Equal<byte>([1], node.Id);
        Assert.Equal<byte>([2], node.Scope);
        Assert.Equal<byte>([3], node.TreeId);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Equivalent replacements remain visible across candidate block boundaries.</summary>
    /// <param name="candidates">The count immediately before, at, or after a complete candidate block.</param>
    [Theory]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    public void LastEntryReplacementRemainsValidAcrossCandidateBlocks(
        int candidates
    )
    {
        // Arrange
        using var context = new BinaryContext();
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        var nodes = CreateBinaryCandidates(candidates);
        context.AttachRange(nodes);
        using var guard = CaptureBinary(context);
        var last = nodes[^1];
        var replacement = last.TreeId.ToArray();

        // Act
        last.TreeId = replacement;
        var failure = Record.Exception(guard.VerifyUnchanged);

        // Assert
        Assert.Null(failure);
        Assert.True(guard.HasCandidates);
        Assert.Same(replacement, last.TreeId);
        Assert.Equal(
            candidates,
            context
                .ChangeTracker
                .Entries<BinaryNode>()
                .Count());
        Assert.All(
            context.ChangeTracker.Entries<BinaryNode>(),
            entry => Assert.Equal(EntityState.Unchanged, entry.State));

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Late mutable identity edits are detected on both sides of a candidate block boundary.</summary>
    /// <param name="candidates">The count immediately before, at, or after a complete candidate block.</param>
    [Theory]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    public void LastEntryEditIsRejectedAcrossCandidateBlocks(
        int candidates
    )
    {
        // Arrange
        using var context = new BinaryContext();
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        var nodes = CreateBinaryCandidates(candidates);
        context.AttachRange(nodes);
        using var guard = CaptureBinary(context);
        var originalTrees = nodes
            .Select(node => node.TreeId.ToArray())
            .ToArray();

        // Act
        nodes[^1].TreeId[0] = 9;
        var failure = Record.Exception(guard.VerifyUnchanged);

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(failure).Code);
        Assert.Equal(9, nodes[^1].TreeId[0]);
        Assert.Equal(
            originalTrees.Take(candidates - 1),
            nodes
                .Take(candidates - 1)
                .Select(node => node.TreeId));
        Assert.Equal(
            candidates,
            context
                .ChangeTracker
                .Entries<BinaryNode>()
                .Count());
        Assert.All(
            context.ChangeTracker.Entries<BinaryNode>(),
            entry => Assert.Equal(EntityState.Unchanged, entry.State));

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Creates distinct valid scalar identities without relying on a database or generated keys.</summary>
    private static BinaryNode[] CreateBinaryCandidates(
        int candidates
    ) => Enumerable
        .Range(1, candidates)
        .Select(id => new BinaryNode
        {
            Id = [checked((byte)id)],
            Scope = [2],
            TreeId = [checked((byte)(id + 10))],
            Left = 1,
            Right = 2,
        })
        .ToArray();

    /// <summary>Captures one unaffected binary candidate with a different requested tree.</summary>
    private static NestedSetTrackedIdentityGuard<BinaryNode, byte[], byte[], byte[]> CaptureBinary(
        BinaryContext context
    )
    {
        var metadata = context.Model.FindEntityType(typeof(BinaryNode))!;
        var map = NestedSetMapping<BinaryNode, byte[], byte[]>.For(context, metadata);
        var request = new NestedSetTreeLockRequest<byte[], byte[]>(metadata, [2], [99], NestedSetTreeLockMode.Existing);

        return NestedSetTrackedIdentityGuard<BinaryNode, byte[], byte[], byte[]>.Capture(context, map, [request])
            ?? throw new InvalidOperationException("The explicit mutation must own its tracker guard.");
    }

    /// <summary>Reads the selected mutable role without reflection or erased property values.</summary>
    private static byte[] Identity(
        BinaryNode node,
        string role
    ) => role switch
    {
        "Key" => node.Id,
        "Scope" => node.Scope,
        "TreeId" => node.TreeId,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    /// <summary>Replaces the selected role while retaining its exact binary model type.</summary>
    private static void ReplaceIdentity(
        BinaryNode node,
        string role,
        byte[] replacement
    )
    {
        switch (role)
        {
            case "Key":
                node.Id = replacement;
                break;
            case "Scope":
                node.Scope = replacement;
                break;
            case "TreeId":
                node.TreeId = replacement;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(role));
        }
    }

    /// <summary>Counts actual provider conversions after the measured capture boundary begins.</summary>
    private static int CountProviderConversion(
        CountedIdentity identity
    )
    {
        Interlocked.Increment(ref s_providerConversions);

        return identity.Value;
    }

    /// <summary>Provides a converted identity whose provider work is observable without database commands.</summary>
    private readonly record struct CountedIdentity(int Value);

    /// <summary>Maps two converted identity roles to ordinary scalar integer columns.</summary>
    private sealed class CountedContext : DbContext
    {
        /// <summary>Creates a model-only context whose database connection remains closed.</summary>
        internal CountedContext() : base(
            new DbContextOptionsBuilder<CountedContext>()
                .ConfigureTestWarnings()
                .UseSqlite("Data Source=:memory:")
                .UseNestedSets()
                .Options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var node = modelBuilder.Entity<CountedNode>();
            node.ToTable("TrackedIdentityCountedNodes");
            node
                .Property(value => value.Id)
                .ValueGeneratedNever();
            node
                .Property(value => value.Scope)
                .HasConversion(value => CountProviderConversion(value), value => new CountedIdentity(value));

            node
                .Property(value => value.TreeId)
                .HasConversion(value => CountProviderConversion(value), value => new CountedIdentity(value));

            node.HasNestedSet(builder => builder
                .HasScope(value => value.Scope)
                .HasTreeId(value => value.TreeId)
                .HasParent(value => value.ParentId));
        }
    }

    /// <summary>Maps binary identities with deliberately shallow domain snapshots.</summary>
    private sealed class BinaryContext : DbContext
    {
        /// <summary>Creates a model-only binary hierarchy context.</summary>
        internal BinaryContext() : base(
            new DbContextOptionsBuilder<BinaryContext>()
                .ConfigureTestWarnings()
                .UseSqlite("Data Source=:memory:")
                .UseNestedSets()
                .Options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var node = modelBuilder.Entity<BinaryNode>();
            node.ToTable("TrackedIdentityBinaryNodes");
            var comparer = new ValueComparer<byte[]>(
                (
                        first,
                        second
                    ) => first == second
                    || (first != null
                        && second != null
                        && first
                            .AsEnumerable()
                            .SequenceEqual(second)),
                value => value.Length,
                value => value);

            // WHY: Domain comparers may intentionally avoid copying arrays. The guard must independently own
            // every mutable identity snapshot rather than trusting these model-level snapshot semantics.
            node
                .Property(value => value.Id)
                .ValueGeneratedNever()
                .HasMaxLength(8)
                .Metadata
                .SetValueComparer(comparer);

            node
                .Property(value => value.Scope)
                .HasMaxLength(8)
                .Metadata
                .SetValueComparer(comparer);
            node
                .Property(value => value.TreeId)
                .HasMaxLength(8)
                .Metadata
                .SetValueComparer(comparer);
            node
                .Property(value => value.ParentId)
                .HasMaxLength(8);
            node.HasNestedSet(builder => builder
                .HasScope(value => value.Scope)
                .HasTreeId(value => value.TreeId)
                .HasParent(value => value.ParentId));
        }
    }

    /// <summary>Separates counted provider roles from the native integer node key.</summary>
    private sealed class CountedNode : IScopedNestedSetNode<int, CountedIdentity, CountedIdentity>
    {
        /// <inheritdoc />
        public int Id { get; set; }

        /// <inheritdoc />
        public CountedIdentity Scope { get; set; }

        /// <inheritdoc />
        public CountedIdentity TreeId { get; set; }

        /// <summary>Gets or sets the optional direct parent key.</summary>
        public int? ParentId { get; set; }

        /// <inheritdoc />
        public long Left { get; set; }

        /// <inheritdoc />
        public long Right { get; set; }

        /// <inheritdoc />
        public int Depth { get; set; }

        /// <inheritdoc />
        public long Position { get; set; }
    }

    /// <summary>Exposes independently mutable binary roles for late verification.</summary>
    private sealed class BinaryNode : IScopedNestedSetNode<byte[], byte[], byte[]>
    {
        /// <inheritdoc />
        public byte[] Id { get; set; } = [];

        /// <inheritdoc />
        public byte[] Scope { get; set; } = [];

        /// <inheritdoc />
        public byte[] TreeId { get; set; } = [];

        /// <summary>Gets or sets the optional direct parent key.</summary>
        public byte[]? ParentId { get; set; }

        /// <inheritdoc />
        public long Left { get; set; }

        /// <inheritdoc />
        public long Right { get; set; }

        /// <inheritdoc />
        public int Depth { get; set; }

        /// <inheritdoc />
        public long Position { get; set; }
    }
}

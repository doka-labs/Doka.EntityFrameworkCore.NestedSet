namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies exact-model tree stores without opening a database connection.</summary>
public sealed class TypedTreeStoreTests
{
    /// <summary>A mismatched generic tree type fails before any database connection can open.</summary>
    [Fact]
    public void TreeTypeMismatchIsRejectedBeforeDatabaseAccess()
    {
        // Arrange
        using var context = new BinaryStoreContext();
        var metadata = context.Model.FindEntityType(typeof(BinaryStoreNode))!;

        // Act
        var failure = Record.Exception(() =>
            new NestedSetStore<BinaryStoreNode, int, Guid, byte[]>(context, metadata, [1], Guid.Empty));

        // Assert
        var mismatch = Assert.IsType<ArgumentException>(failure);
        Assert.Equal("treeId", mismatch.ParamName);
        Assert.Contains(typeof(byte[]).FullName!, mismatch.Message, StringComparison.Ordinal);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Cached metadata from another model cannot be bound to the caller's context.</summary>
    [Fact]
    public void ForeignModelMetadataIsRejectedBeforeDatabaseAccess()
    {
        // Arrange
        using var source = new BinaryStoreContext();
        using var target = new BinaryStoreContext(alternateTable: true);
        var foreignMetadata = source.Model.FindEntityType(typeof(BinaryStoreNode))!;

        // Act
        var failure = Record.Exception(() =>
            new NestedSetStore<BinaryStoreNode, int, byte[], byte[]>(target, foreignMetadata, [1], [2]));

        // Assert
        var mismatch = Assert.IsType<ArgumentException>(failure);
        Assert.Equal("entityType", mismatch.ParamName);
        Assert.Contains("context model", mismatch.Message, StringComparison.Ordinal);
        Assert.Equal(ConnectionState.Closed, target.Database.GetDbConnection().State);
    }

    /// <summary>Caller mutation cannot change binary store identities, lock requests, or SQL parameters.</summary>
    [Fact]
    public void BinaryIdentitiesAreSnapshottedForQueriesAndRegistryLocks()
    {
        // Arrange
        using var context = new BinaryStoreContext();
        var metadata = context.Model.FindEntityType(typeof(BinaryStoreNode))!;
        byte[] scope = [1, 2, 3, 4];
        byte[] treeId = [5, 6, 7, 8];
        var store = new NestedSetStore<BinaryStoreNode, int, byte[], byte[]>(context, metadata, scope, treeId);

        // Act
        scope[0] = 9;
        treeId[0] = 10;
        var request = store.LockRequest(NestedSetTreeLockMode.Existing);
        var sql = store.Nodes.ToQueryString();

        // Assert
        Assert.NotSame(scope, store.Scope);
        Assert.NotSame(treeId, store.TreeId);
        Assert.Equal<byte>([1, 2, 3, 4], store.Scope);
        Assert.Equal<byte>([5, 6, 7, 8], store.TreeId);
        Assert.Equal(store.Scope, Assert.IsType<byte[]>(request.Scope));
        Assert.Equal(store.TreeId, Assert.IsType<byte[]>(request.TreeId));
        Assert.Contains("01020304", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("05060708", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("09020304", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0A060708", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>A named shared mapping binds its own table and typed property-bag identities.</summary>
    [Fact]
    public void NamedSharedStoreUsesTheSelectedMapping()
    {
        // Arrange
        using var context = new BinaryStoreContext();
        var metadata = context.Model.FindEntityType(BinaryStoreContext.SecondSharedEntity)!;

        // Act
        var store = new NestedSetStore<Dictionary<string, object>, int, byte[], byte[]>(context, metadata, [1], [2]);

        var sql = store.Nodes.ToQueryString();

        // Assert
        Assert.Same(metadata, store.Map.EntityType);
        Assert.Contains("TypedTreeStoreSharedSecond", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("TypedTreeStoreSharedFirst", sql, StringComparison.Ordinal);
        Assert.Contains(nameof(BinaryStoreNode.TreeId), sql, StringComparison.Ordinal);
        Assert.Contains(nameof(BinaryStoreNode.Scope), sql, StringComparison.Ordinal);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Same-model metadata cannot select another hierarchy with an incompatible entity type.</summary>
    [Fact]
    public void DifferentHierarchyMetadataIsRejectedAfterCacheWarmup()
    {
        // Arrange
        using var context = new BinaryStoreContext();
        var metadata = context.Model.FindEntityType(BinaryStoreContext.SecondSharedEntity)!;
        _ = new NestedSetStore<Dictionary<string, object>, int, byte[], byte[]>(context, metadata, [1], [2]);

        // Act
        var failure = Record.Exception(() =>
            new NestedSetStore<BinaryStoreNode, int, byte[], byte[]>(context, metadata, [1], [2]));

        // Assert
        var mismatch = Assert.IsType<ArgumentException>(failure);
        Assert.Equal("entityType", mismatch.ParamName);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Maps binary identities and named property bags using a variant-aware model cache.</summary>
    private sealed class BinaryStoreContext : DbContext, ITestModelVariant
    {
        internal const string SecondSharedEntity = "TypedTreeStoreSharedSecond";
        private readonly bool _alternateTable;

        /// <summary>Creates a model-only context with optional distinct physical mapping.</summary>
        internal BinaryStoreContext(
            bool alternateTable = false
        ) : base(
            new DbContextOptionsBuilder<BinaryStoreContext>()
                .ConfigureTestWarnings()
                .UseSqlite("Data Source=:memory:")
                .UseNestedSets()
                .ReplaceService<IModelCacheKeyFactory, TestModelCacheKeyFactory>()
                .Options)
        {
            _alternateTable = alternateTable;
        }

        /// <inheritdoc />
        object ITestModelVariant.ModelVariant => _alternateTable;

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var node = modelBuilder.Entity<BinaryStoreNode>();
            node.ToTable(_alternateTable ? "TypedTreeStoreAlternate" : "TypedTreeStoreNodes");
            node
                .Property(value => value.Id)
                .ValueGeneratedNever();
            node
                .Property(value => value.TreeId)
                .HasMaxLength(8);
            node
                .Property(value => value.Scope)
                .HasMaxLength(8);
            node.HasNestedSet(builder => builder
                .HasTreeId(value => value.TreeId)
                .HasScope(value => value.Scope)
                .HasParent(value => value.ParentId));

            AddShared(modelBuilder, "TypedTreeStoreSharedFirst");
            AddShared(modelBuilder, SecondSharedEntity);
        }

        /// <summary>Configures exact scalar hierarchy roles on an independently named shared entity.</summary>
        private static void AddShared(
            ModelBuilder modelBuilder,
            string entityName
        )
        {
            var entity = modelBuilder.SharedTypeEntity<Dictionary<string, object>>(entityName);
            entity.ToTable(entityName);
            entity
                .IndexerProperty<int>(nameof(BinaryStoreNode.Id))
                .ValueGeneratedNever();

            // WHY: Property-bag indexers have no CLR nullability metadata; both identity roles must be required.
            entity
                .IndexerProperty<byte[]>(nameof(BinaryStoreNode.TreeId))
                .HasMaxLength(8)
                .IsRequired();
            entity
                .IndexerProperty<byte[]>(nameof(BinaryStoreNode.Scope))
                .HasMaxLength(8)
                .IsRequired();
            entity.IndexerProperty<int?>(nameof(BinaryStoreNode.ParentId));
            entity.IndexerProperty<long>(nameof(BinaryStoreNode.Left));
            entity.IndexerProperty<long>(nameof(BinaryStoreNode.Right));
            entity.IndexerProperty<int>(nameof(BinaryStoreNode.Depth));
            entity.IndexerProperty<long>(nameof(BinaryStoreNode.Position));
            entity.HasKey(nameof(BinaryStoreNode.Id));
            entity.HasNestedSet(builder => builder
                .HasNodeKey(nameof(BinaryStoreNode.Id))
                .HasTreeId(nameof(BinaryStoreNode.TreeId))
                .HasScope(nameof(BinaryStoreNode.Scope))
                .HasParent(nameof(BinaryStoreNode.ParentId))
                .HasBounds(nameof(BinaryStoreNode.Left), nameof(BinaryStoreNode.Right))
                .HasDepth(nameof(BinaryStoreNode.Depth))
                .HasPosition(nameof(BinaryStoreNode.Position)));
        }
    }

    /// <summary>Separates two independently mutable binary identities from an integer node key.</summary>
    private sealed class BinaryStoreNode : IScopedNestedSetNode<int, byte[], byte[]>
    {
        /// <inheritdoc />
        public int Id { get; set; }

        /// <inheritdoc />
        public byte[] TreeId { get; set; } = [];

        /// <inheritdoc />
        public byte[] Scope { get; set; } = [];

        /// <summary>Gets or sets the optional direct parent identity.</summary>
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
}

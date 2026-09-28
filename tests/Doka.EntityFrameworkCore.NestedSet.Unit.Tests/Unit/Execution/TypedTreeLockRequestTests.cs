namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies typed tree identities and exact mapping ownership before any database access.</summary>
public sealed class TypedTreeLockRequestTests
{
    /// <summary>
    ///     Snapshots both mutable identity roles while exposing the same values to native lock consumers.
    /// </summary>
    [Fact]
    public void BinaryRequestRetainsIndependentTypedSnapshots()
    {
        // Arrange
        using var context = new RequestContext();
        var entityType = context.Model.FindEntityType(RequestContext.FirstHierarchy)!;
        byte[] scope = [1, 2];
        byte[] treeId = [3, 4];
        var request = new NestedSetTreeLockRequest<byte[], byte[]>(
            entityType,
            scope,
            treeId,
            NestedSetTreeLockMode.Existing);

        INestedSetTreeLockRequest native = request;

        // Act
        scope[0] = 9;
        treeId[0] = 8;

        // Assert
        Assert.NotSame(scope, request.Scope);
        Assert.NotSame(treeId, request.TreeId);
        Assert.Equal<byte>([1, 2], request.Scope);
        Assert.Equal<byte>([3, 4], request.TreeId);
        Assert.Same(request.Scope, native.ScopeValue);
        Assert.Same(request.TreeId, native.TreeIdValue);
        Assert.Same(request.Mapping, native.Mapping);
        Assert.Equal(NestedSetTreeLockMode.Existing, native.Mode);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Rejects an incompatible generic identity role before opening the database.</summary>
    /// <param name="wrongScope">Whether Scope rather than TreeId has the incompatible generic type.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenericRoleMismatchIsRejectedBeforeDatabaseAccess(
        bool wrongScope
    )
    {
        // Arrange
        using var context = new RequestContext();
        var entityType = context.Model.FindEntityType(RequestContext.FirstHierarchy)!;

        // Act
        var error = Record.Exception(() =>
        {
            if (wrongScope)
            {
                _ = new NestedSetTreeLockRequest<byte[], Guid>(
                    entityType,
                    Guid.Empty,
                    [1],
                    NestedSetTreeLockMode.Existing);
            }
            else
            {
                _ = new NestedSetTreeLockRequest<Guid, byte[]>(
                    entityType,
                    [1],
                    Guid.Empty,
                    NestedSetTreeLockMode.Existing);
            }
        });

        // Assert
        Assert.Equal(wrongScope ? "Scope" : "TreeId", Assert.IsType<ArgumentException>(error).ParamName);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>Represents an omitted Scope using a typed marker and erases only its native parameter view.</summary>
    [Fact]
    public void ScopelessRequestUsesMarkerWithoutInventingAStoredScope()
    {
        // Arrange
        using var context = new RequestContext();
        var entityType = context.Model.FindEntityType(typeof(ScopelessNode))!;

        // Act
        var request = new NestedSetTreeLockRequest<Guid, NestedSetNoScope>(
            entityType,
            default,
            Guid.Empty,
            NestedSetTreeLockMode.New);

        // Assert
        Assert.Equal(default, request.Scope);
        Assert.Null(((INestedSetTreeLockRequest)request).ScopeValue);
        Assert.Null(request.Mapping.SourceScope);
        Assert.Equal(Guid.Empty, request.TreeId);
        Assert.Equal(NestedSetTreeLockMode.New, request.Mode);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Rejects an unrelated role even when the mapped hierarchy omits its Scope column.</summary>
    [Fact]
    public void ScopelessRequestRejectsAnUnrelatedGenericScopeRole()
    {
        // Arrange
        using var context = new RequestContext();
        var entityType = context.Model.FindEntityType(typeof(ScopelessNode))!;

        // Act
        var error = Record.Exception(() => new NestedSetTreeLockRequest<Guid, int>(
            entityType,
            0,
            Guid.Empty,
            NestedSetTreeLockMode.Existing));

        // Assert
        Assert.Equal("scope", Assert.IsType<ArgumentException>(error).ParamName);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Rejects another named hierarchy sharing the same CLR type and exact generic identity roles.</summary>
    /// <param name="readOnly">Whether the consistent-read rather than mutation boundary is exercised.</param>
    /// <returns>A task that completes after verifying no delegate or database command was executed.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameModelForeignNamedHierarchyIsRejectedBeforeDatabaseAccess(
        bool readOnly
    )
    {
        // Arrange
        await using var context = new RequestContext();
        var first = context.Model.FindEntityType(RequestContext.FirstHierarchy)!;
        var second = context.Model.FindEntityType(RequestContext.SecondHierarchy)!;
        var executor = new NestedSetMutationExecutor<Dictionary<string, object>, int, byte[], byte[]>(context, first);
        var request = new NestedSetTreeLockRequest<byte[], byte[]>(second, [1], [2], NestedSetTreeLockMode.Existing);

        var attempts = 0;

        // Act
        var error = await Record.ExceptionAsync(() => readOnly
            ? executor.ExecuteReadAsync(Operation, [request], CancellationToken.None)
            : executor.ExecuteAsync(Operation, [request], CancellationToken.None));

        // Assert
        Assert.Equal("requests", Assert.IsType<ArgumentException>(error).ParamName);
        Assert.NotSame(request.Mapping, NestedSetTreeRegistryMapping.For(first));
        Assert.Equal(0, attempts);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Empty(context.ChangeTracker.Entries());
        return;

        Task Operation(
            CancellationToken token
        )
        {
            token.ThrowIfCancellationRequested();
            attempts++;

            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     Provides two named binary hierarchies and a separate scopeless hierarchy for model-only checks.
    /// </summary>
    private sealed class RequestContext : DbContext
    {
        internal const string FirstHierarchy = "TypedRequestFirst";
        internal const string SecondHierarchy = "TypedRequestSecond";

        /// <summary>Creates a model-only context with the production registration and a closed connection.</summary>
        internal RequestContext() : base(
            new DbContextOptionsBuilder<RequestContext>()
                .ConfigureTestWarnings()
                .UseSqlite("Data Source=:memory:")
                .UseNestedSets()
                .Options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            ConfigureShared(modelBuilder, FirstHierarchy);
            ConfigureShared(modelBuilder, SecondHierarchy);
            modelBuilder
                .Entity<ScopelessNode>()
                .Property(node => node.Id)
                .ValueGeneratedNever();
            modelBuilder
                .Entity<ScopelessNode>()
                .HasNestedSet(node => node.HasParent(value => value.ParentId));
        }

        /// <summary>Configures identical CLR roles on independently named physical hierarchies.</summary>
        private static void ConfigureShared(
            ModelBuilder modelBuilder,
            string name
        )
        {
            var entity = modelBuilder.SharedTypeEntity<Dictionary<string, object>>(name);
            entity.ToTable(name);
            entity
                .IndexerProperty<int>("Id")
                .ValueGeneratedNever();

            // WHY: Property bags have no CLR nullability contract, so both binary identities are explicitly required.
            entity
                .IndexerProperty<byte[]>("TreeId")
                .HasMaxLength(8)
                .IsRequired();
            entity
                .IndexerProperty<byte[]>("Scope")
                .HasMaxLength(8)
                .IsRequired();
            entity.IndexerProperty<int?>("ParentId");
            entity.IndexerProperty<long>("Left");
            entity.IndexerProperty<long>("Right");
            entity.IndexerProperty<int>("Depth");
            entity.IndexerProperty<long>("Position");
            entity.HasKey("Id");
            entity.HasNestedSet(node => node
                .HasNodeKey("Id")
                .HasTreeId("TreeId")
                .HasScope("Scope")
                .HasParent("ParentId")
                .HasBounds("Left", "Right")
                .HasDepth("Depth")
                .HasPosition("Position"));
        }
    }

    /// <summary>Models a hierarchy whose configuration has no Scope role or column.</summary>
    private sealed class ScopelessNode : INestedSetNode<int, Guid>
    {
        /// <inheritdoc />
        public int Id { get; set; }

        /// <inheritdoc />
        public Guid TreeId { get; set; }

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

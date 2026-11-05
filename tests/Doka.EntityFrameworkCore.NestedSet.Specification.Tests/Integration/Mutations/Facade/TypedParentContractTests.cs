using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies assigned default scalar keys remain present parents rather than root sentinels.</summary>
public abstract class TypedParentContractTests : ProviderTest
{
    private static readonly Guid s_source = Guid.Parse("41000000-0000-0000-0000-000000000001");
    private static readonly Guid s_target = Guid.Parse("41000000-0000-0000-0000-000000000002");
    private static readonly Guid s_child = Guid.Parse("42000000-0000-0000-0000-000000000001");
    private static readonly Guid s_other = Guid.Parse("42000000-0000-0000-0000-000000000002");
    private readonly RelationalFixture _fixture;
    private readonly ModelCompatibilityDatabase _convertedFixture;

    /// <summary>Creates assigned-key contracts on the existing relational fixture.</summary>
    /// <param name="fixture">The owner of the reusable provider databases.</param>
    /// <param name="convertedFixture">The existing database owner for the private converted-key model.</param>
    protected TypedParentContractTests(
        IProviderFixture<RelationalFixture> fixture,
        IProviderFixture<ModelCompatibilityDatabase> convertedFixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
        _convertedFixture = convertedFixture.Value;
    }

    /// <summary>Provides assigned and converted default-key representations for the owning provider fixture.</summary>
    public static IEnumerable<TheoryDataRow<string>> Cases()
    {
        yield return new TheoryDataRow<string>("Int");
        yield return new TheoryDataRow<string>("Guid");
        yield return new TheoryDataRow<string>("OperatorFree");
    }

    /// <summary>Insertion and parent queries preserve a present parent whose assigned key is the default.</summary>
    /// <param name="keyType">The assigned scalar key representation.</param>
    /// <returns>A task that completes after verifying root absence and child parent presence separately.</returns>
    [Theory]
    [MemberData(nameof(Cases))]
    public Task InsertUnderDefaultKeyPreservesParentPresence(
        string keyType
    ) => keyType switch
    {
        "Int" => InsertAsync(Engine, 1, IntegerNode, node => node.ParentId,
            cancellationToken: TestContext.Current.CancellationToken),
        "Guid" => InsertAsync(Engine, s_child, GuidNodeFor, node => node.ParentId,
            cancellationToken: TestContext.Current.CancellationToken),
        "OperatorFree" => InsertAsync(Engine, new OperatorFreeKey(1), ConvertedNode, node => node.ParentId,
            cancellationToken: TestContext.Current.CancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(keyType)),
    };

    /// <summary>A move to a default-key root retains a present typed parent and the destination tree.</summary>
    /// <param name="keyType">The assigned scalar key representation.</param>
    /// <returns>A task that completes after verifying both source and destination remain valid.</returns>
    [Theory]
    [MemberData(nameof(Cases))]
    public Task ReparentToDefaultKeyPreservesParentPresence(
        string keyType
    ) => keyType switch
    {
        "Int" => ReparentAsync(Engine, 1, 2, IntegerNode, node => node.ParentId,
            cancellationToken: TestContext.Current.CancellationToken),
        "Guid" => ReparentAsync(Engine, s_child, s_other, GuidNodeFor, node => node.ParentId,
            cancellationToken: TestContext.Current.CancellationToken),
        "OperatorFree" => ReparentAsync(
            Engine,
            new OperatorFreeKey(1),
            new OperatorFreeKey(2),
            ConvertedNode,
            node => node.ParentId, cancellationToken: TestContext.Current.CancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(keyType)),
    };

    /// <summary>Detaching beneath a default-key root changes parent presence to genuine absence.</summary>
    /// <param name="keyType">The assigned scalar key representation.</param>
    /// <returns>A task that completes after verifying the original and detached roots have no parent.</returns>
    [Theory]
    [MemberData(nameof(Cases))]
    public Task DetachFromDefaultKeyCreatesAbsentParent(
        string keyType
    ) => keyType switch
    {
        "Int" => DetachAsync(Engine, 1, 2, IntegerNode, node => node.ParentId,
            cancellationToken: TestContext.Current.CancellationToken),
        "Guid" => DetachAsync(Engine, s_child, s_other, GuidNodeFor, node => node.ParentId,
            cancellationToken: TestContext.Current.CancellationToken),
        "OperatorFree" => DetachAsync(
            Engine,
            new OperatorFreeKey(1),
            new OperatorFreeKey(2),
            ConvertedNode,
            node => node.ParentId, cancellationToken: TestContext.Current.CancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(keyType)),
    };

    /// <summary>Uses existing ValueGeneratedNever models without introducing a default-key sentinel.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private async Task InsertAsync<TEntity, TKey>(
        string engine,
        TKey childKey,
        Func<TKey, TEntity> create,
        Func<TEntity, TKey?> parent,
        CancellationToken cancellationToken
    )
        where TEntity : class, IScopedNestedSetNode<TKey, Guid, int>
        where TKey : struct
    {
        // Arrange
        await using var context = await CreateContextAsync<TEntity>(engine, cancellationToken: cancellationToken);
        var hierarchy = context
            .NestedSet<TEntity>()
            .ForScope(7);

        await hierarchy.InsertRootAsync(create(default), s_source, cancellationToken);
        var child = create(childKey);

        // Act
        await hierarchy.InsertChildAsync(child, default(TKey), cancellationToken);

        // Assert
        Assert.True(parent(child).HasValue);
        Assert.Equal(default, parent(child).GetValueOrDefault());
        Assert.Equal(
            default,
            (await hierarchy
                .ParentOf(childKey)
                .SingleAsync(cancellationToken)).Id);

        var stored = await hierarchy
            .InTree(s_source)
            .Nodes
            .ToArrayAsync(cancellationToken);

        var root = Assert.Single(stored, node => EqualityComparer<TKey>.Default.Equals(node.Id, default));
        Assert.False(parent(root).HasValue);
        Assert.Equal(
            childKey,
            (await hierarchy
                .ChildrenOf(default(TKey))
                .SingleAsync(cancellationToken)).Id);
        Assert.Equal((1L, 4L, 0), (root.Left, root.Right, root.Depth));
        Assert.True(
            (await hierarchy
                .InTree(s_source)
                .ValidateAsync(NestedSetValidationLevel.Full, cancellationToken)).IsValid);
    }

    /// <summary>Distinguishes a default-valued destination parent from null during a cross-tree move.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private async Task ReparentAsync<TEntity, TKey>(
        string engine,
        TKey childKey,
        TKey otherRootKey,
        Func<TKey, TEntity> create,
        Func<TEntity, TKey?> parent,
        CancellationToken cancellationToken
    )
        where TEntity : class, IScopedNestedSetNode<TKey, Guid, int>
        where TKey : struct
    {
        // Arrange
        await using var context = await CreateContextAsync<TEntity>(engine, cancellationToken: cancellationToken);
        var hierarchy = context
            .NestedSet<TEntity>()
            .ForScope(7);

        await hierarchy.InsertRootAsync(create(default), s_target, cancellationToken);
        await hierarchy.InsertRootAsync(create(otherRootKey), s_source, cancellationToken);
        await hierarchy.InsertChildAsync(create(childKey), otherRootKey, cancellationToken);

        // Act
        await hierarchy.MoveToAsync(childKey, default(TKey), cancellationToken);

        // Assert
        var child = await hierarchy
            .ChildrenOf(default(TKey))
            .SingleAsync(cancellationToken);

        Assert.Equal(childKey, child.Id);
        Assert.True(parent(child).HasValue);
        Assert.Equal(default, parent(child).GetValueOrDefault());
        Assert.Equal((s_target, 2L, 3L, 1), (child.TreeId, child.Left, child.Right, child.Depth));
        Assert.Equal(
            default,
            (await hierarchy
                .ParentOf(childKey)
                .SingleAsync(cancellationToken)).Id);
        Assert.Empty(
            await hierarchy
                .ChildrenOf(otherRootKey)
                .ToArrayAsync(cancellationToken));
        Assert.True(
            (await hierarchy
                .InTree(s_source)
                .ValidateAsync(NestedSetValidationLevel.Full, cancellationToken)).IsValid);
        Assert.True(
            (await hierarchy
                .InTree(s_target)
                .ValidateAsync(NestedSetValidationLevel.Full, cancellationToken)).IsValid);
    }

    /// <summary>Verifies parent absence is explicit when a typed subtree becomes a separate root.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private async Task DetachAsync<TEntity, TKey>(
        string engine,
        TKey childKey,
        TKey grandchildKey,
        Func<TKey, TEntity> create,
        Func<TEntity, TKey?> parent,
        CancellationToken cancellationToken
    )
        where TEntity : class, IScopedNestedSetNode<TKey, Guid, int>
        where TKey : struct
    {
        // Arrange
        await using var context = await CreateContextAsync<TEntity>(engine, cancellationToken: cancellationToken);
        var hierarchy = context
            .NestedSet<TEntity>()
            .ForScope(7);

        await hierarchy.InsertRootAsync(create(default), s_source, cancellationToken);
        await hierarchy.InsertChildAsync(create(childKey), default(TKey), cancellationToken);
        await hierarchy.InsertChildAsync(create(grandchildKey), childKey, cancellationToken);

        // Act
        await hierarchy.DetachAsTreeAsync(childKey, s_target, cancellationToken);

        // Assert
        var source = await hierarchy
            .InTree(s_source)
            .Nodes
            .SingleAsync(cancellationToken);

        Assert.Equal(default(TKey), source.Id);
        Assert.False(parent(source).HasValue);
        Assert.Equal((1L, 2L, 0), (source.Left, source.Right, source.Depth));
        var detached = await hierarchy
            .InTree(s_target)
            .Nodes
            .ToArrayAsync(cancellationToken);

        Assert.Equal(2, detached.Length);
        Assert.False(parent(detached[0]).HasValue);
        Assert.True(parent(detached[1]).HasValue);
        Assert.Equal(childKey, parent(detached[1]).GetValueOrDefault());
        Assert.Empty(
            await hierarchy
                .ParentOf(childKey)
                .ToArrayAsync(cancellationToken));
        Assert.Empty(
            await hierarchy
                .ChildrenOf(default(TKey))
                .ToArrayAsync(cancellationToken));
        Assert.True(
            (await hierarchy
                .InTree(s_target)
                .ValidateAsync(NestedSetValidationLevel.Full, cancellationToken)).IsValid);
    }

    /// <summary>Creates an assigned integer key, including zero, on the existing restrictive-FK model.</summary>
    private static ConstrainedNode IntegerNode(
        int key
    ) => new() { Id = key };

    /// <summary>Creates an assigned Guid key, including Guid.Empty, on the existing Guid model.</summary>
    private static GuidNode GuidNodeFor(
        Guid key
    ) => new() { Id = key };

    /// <summary>Creates the operator-free converted model while reusing the existing provider database owner.</summary>
    private static OperatorFreeNode ConvertedNode(
        OperatorFreeKey key
    ) => new() { Id = key };

    /// <summary>Resets only the model used by this scalar-key representation.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private async Task<DbContext> CreateContextAsync<TEntity>(
        string engine,
        CancellationToken cancellationToken
    )
    {
        if (typeof(TEntity) != typeof(OperatorFreeNode))
        {
            var database = await _fixture.ResetAsync(engine, cancellationToken: cancellationToken);

            return database.CreateContext();
        }

        var context = await _convertedFixture.CreateContextAsync<OperatorFreeContext>(
            engine,
            static options => new OperatorFreeContext(options), cancellationToken: cancellationToken);

        await context
            .Set<OperatorFreeNode>()
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(node => node.ParentId, (OperatorFreeKey?)null),
                cancellationToken);

        await context
            .Set<OperatorFreeNode>()
            .ExecuteDeleteAsync(cancellationToken);

        await context.ClearNestedSetTreeRegistriesAsync(cancellationToken);

        return context;
    }

    /// <summary>An immutable converted scalar supplies typed equality without equality operators.</summary>
    private readonly struct OperatorFreeKey(int value) : IEquatable<OperatorFreeKey>
    {
        /// <summary>Gets the native integer identity persisted by the model converter.</summary>
        public int Value { get; } = value;

        /// <inheritdoc />
        public bool Equals(
            OperatorFreeKey other
        ) => Value.Equals(other.Value);

        /// <inheritdoc />
        public override bool Equals(
            object? other
        ) => other is OperatorFreeKey key && Equals(key);

        /// <inheritdoc />
        public override int GetHashCode() => Value.GetHashCode();
    }

    /// <summary>Represents an assigned converted key whose nullable parent distinguishes zero from absence.</summary>
    private sealed class OperatorFreeNode : IScopedNestedSetNode<OperatorFreeKey, Guid, int>
    {
        /// <inheritdoc />
        public OperatorFreeKey Id { get; set; }

        /// <inheritdoc />
        public Guid TreeId { get; set; }

        /// <inheritdoc />
        public int Scope { get; set; }

        /// <summary>Gets or sets the explicitly optional converted parent key.</summary>
        public OperatorFreeKey? ParentId { get; set; }

        /// <inheritdoc />
        public long Left { get; set; }

        /// <inheritdoc />
        public long Right { get; set; }

        /// <inheritdoc />
        public int Depth { get; set; }

        /// <inheritdoc />
        public long Position { get; set; }
    }

    /// <summary>Maps operator-free key and parent values through the same bounded native integer type.</summary>
    private sealed class OperatorFreeContext(DbContextOptions<OperatorFreeContext> options) : DbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var node = modelBuilder.Entity<OperatorFreeNode>();
            var converter =
                new ValueConverter<OperatorFreeKey, int>(
                    key => key.Value,
                    value => new OperatorFreeKey(value));

            node.ToTable("OperatorFreeTypedParentNodes");
            node.HasKey(value => value.Id);
            node
                .Property(value => value.Id)
                .HasConversion(converter)
                .ValueGeneratedNever();
            node
                .Property(value => value.ParentId)
                .HasConversion(converter);
            node.HasNestedSet(builder => builder
                .HasTreeId(value => value.TreeId)
                .HasScope(value => value.Scope)
                .HasParent(value => value.ParentId));
        }
    }
}

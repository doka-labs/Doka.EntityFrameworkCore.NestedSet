using Doka.EntityFrameworkCore.NestedSet.Providers;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class UnicodeIdentityLengthTests
{
    /// <summary>Preserves the bounded PostgreSQL bpchar alias and its character limit for both roles.</summary>
    /// <param name="scopeRole">Whether Scope rather than TreeId carries the tested value.</param>
    /// <param name="overflow">Whether one scalar exceeds the configured character boundary.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PostgreSqlBpcharAliasUsesScalarBoundary(
        bool scopeRole,
        bool overflow
    )
    {
        // Arrange
        using var context = new BpcharContext();
        var entityType = context.Model.FindEntityType(typeof(BpcharNode))!;
        var input = "\U0001f600\U0001f601\U0001f602\U0001f603\U0001f604\U0001f605\U0001f606\U0001f607"
            + (overflow ? "x" : string.Empty);

        NestedSetTreeLockRequest<string, string>? request = null;

        // Act
        var error = Record.Exception(() => request = new NestedSetTreeLockRequest<string, string>(
            entityType,
            scopeRole ? input : "scope",
            scopeRole ? "tree" : input,
            NestedSetTreeLockMode.Existing));

        // Assert
        if (overflow)
        {
            Assert.Equal(scopeRole ? "Scope" : "TreeId", Assert.IsType<ArgumentException>(error).ParamName);
            Assert.Null(request);
        }
        else
        {
            Assert.Null(error);
            Assert.NotNull(request);
            Assert.Same(input, scopeRole ? request.Scope : request.TreeId);
        }

        AssertNoDatabaseAccess(context);
    }

    /// <summary>Recognizes the concrete mapping and alias retained by an EF runtime mapping clone.</summary>
    /// <param name="scopeRole">Whether the cloned property is Scope rather than TreeId.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PostgreSqlBpcharRuntimeCloneRetainsCharacterPolicy(
        bool scopeRole
    )
    {
        // Arrange
        using var context = new BpcharContext();
        var entityType = context.Model.FindEntityType(typeof(BpcharNode))!;
        var mapping = entityType.FindProperty(scopeRole ? "Scope" : "TreeId")!.GetRelationalTypeMapping();
        var clone = mapping.WithStoreTypeAndSize(mapping.StoreType, mapping.Size);

        // Act
        var scalarLength = NestedSetProviderCapabilities.Resolve(context).UsesUnicodeScalarLength(clone);

        // Assert
        Assert.True(scalarLength);
        Assert.Equal("bpchar", clone.StoreTypeNameBase);
        Assert.Equal(
            "Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.Mapping.NpgsqlStringTypeMapping",
            clone.GetType().FullName);
        Assert.Equal(mapping.GetType(), clone.GetType());
        Assert.True(NestedSetTreeRegistryMapping.For(entityType).ScopeUsesUnicodeScalarLength);
        Assert.True(NestedSetTreeRegistryMapping.For(entityType).TreeIdUsesUnicodeScalarLength);
        AssertNoDatabaseAccess(context);
    }

    /// <summary>Finalizes an explicitly bounded PostgreSQL character alias without contacting a server.</summary>
    private sealed class BpcharContext : DbContext
    {
        /// <inheritdoc />
        protected override void OnConfiguring(
            DbContextOptionsBuilder optionsBuilder
        ) => optionsBuilder
            .UseNpgsql(
                "Host=127.0.0.1;Port=1;Database=unicode;Username=unused;Password=unused",
                provider => provider.UseNestedSets())
            .ConfigureTestWarnings();

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var entity = modelBuilder.Entity<BpcharNode>();
            entity
                .Property(node => node.Id)
                .ValueGeneratedNever();
            entity
                .Property(node => node.Scope)
                .HasColumnType("bpchar(8)")
                .HasMaxLength(8);
            entity
                .Property(node => node.TreeId)
                .HasColumnType("bpchar(8)")
                .HasMaxLength(8);
            entity.HasNestedSet(node => node
                .HasScope(value => value.Scope)
                .HasParent(value => value.ParentId));
        }
    }

    /// <summary>Provides explicit character identities while retaining ordinary integer parent links.</summary>
    private sealed class BpcharNode : INestedSetNode<int, string>
    {
        /// <inheritdoc />
        public int Id { get; set; }

        /// <summary>Gets or sets the bounded Scope partition.</summary>
        public string Scope { get; set; } = string.Empty;

        /// <inheritdoc />
        public string TreeId { get; set; } = string.Empty;

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
}

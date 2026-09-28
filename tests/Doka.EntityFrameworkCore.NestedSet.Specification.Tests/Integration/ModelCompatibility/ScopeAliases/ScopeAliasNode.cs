namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Represents a hierarchy with a native integer NodeKey and a case-insensitive text scope.</summary>
internal sealed class ScopeAliasNode
{
    /// <summary>Gets or sets the native integer hierarchy identity.</summary>
    internal int Id { get; set; }

    /// <summary>Gets or sets the tenant whose database comparison ignores letter case.</summary>
    internal string Scope { get; set; } = "";

    /// <summary>Gets or sets the stable tree identity.</summary>
    internal Guid TreeId { get; set; }

    /// <summary>Gets or sets the direct parent identity.</summary>
    internal int? ParentId { get; set; }

    /// <summary>Gets or sets the inclusive left boundary.</summary>
    internal long Left { get; set; }

    /// <summary>Gets or sets the inclusive right boundary.</summary>
    internal long Right { get; set; }

    /// <summary>Gets or sets the zero-based depth.</summary>
    internal int Depth { get; set; }

    /// <summary>Gets or sets the dense sibling position.</summary>
    internal long Position { get; set; }

    /// <summary>Gets or sets the configured sibling ordering value.</summary>
    internal string Name { get; set; } = "";

    /// <summary>Copies persisted values under another spelling of the same database scope.</summary>
    internal ScopeAliasNode WithScope(
        string scope
    ) => new()
    {
        Id = Id,
        Scope = scope,
        TreeId = TreeId,
        ParentId = ParentId,
        Left = Left,
        Right = Right,
        Depth = Depth,
        Position = Position,
        Name = Name,
    };
}

/// <summary>Uses NodeKey alone as the EF key, so refresh needs no scope correlation.</summary>
internal sealed class ScalarScopeAliasContext : DbContext
{
    /// <summary>Creates the scalar-identity alias context.</summary>
    internal ScalarScopeAliasContext(
        DbContextOptions<ScalarScopeAliasContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    ) => ScopeAliasModel.Configure(
        modelBuilder,
        this,
        "ScalarScopeAliasNodes",
        "scalar_scope_alias_ci",
        node =>
        {
            node.HasKey(entity => entity.Id);
            node
                .HasOne<ScopeAliasNode>()
                .WithMany()
                .HasPrincipalKey(entity => new
                {
                    entity.Scope,
                    entity.Id,
                })
                .HasForeignKey(entity => new
                {
                    entity.Scope,
                    entity.ParentId,
                })
                .OnDelete(DeleteBehavior.Restrict);
        });
}

/// <summary>Uses Scope and NodeKey as the EF key, so equal NodeKeys can exist in other scopes.</summary>
internal sealed class ScopedScopeAliasContext : DbContext
{
    /// <summary>Creates the scope-qualified identity alias context.</summary>
    internal ScopedScopeAliasContext(
        DbContextOptions<ScopedScopeAliasContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    ) => ScopeAliasModel.Configure(
        modelBuilder,
        this,
        "ScopedScopeAliasNodes",
        "scoped_scope_alias_ci",
        node =>
        {
            node.HasKey(entity => new
            {
                entity.Scope,
                entity.Id,
            });
            node
                .HasOne<ScopeAliasNode>()
                .WithMany()
                .HasForeignKey(entity => new
                {
                    entity.Scope,
                    entity.ParentId
                })
                .OnDelete(DeleteBehavior.Restrict);
        });
}

/// <summary>Shares the case-insensitive scope mapping between both identity forms.</summary>
internal static class ScopeAliasModel
{
    /// <summary>Maps the table, native case-insensitive scope collation, identity, and nested-set roles.</summary>
    internal static void Configure(
        ModelBuilder modelBuilder,
        DbContext context,
        string table,
        string postgresCollation,
        Action<EntityTypeBuilder<ScopeAliasNode>> identity
    )
    {
        if (context.Database.IsNpgsql())
        {
            modelBuilder.HasCollation(
                postgresCollation,
                locale: "und-u-ks-level2",
                provider: "icu",
                deterministic: false);
        }

        var collation = context.Database.IsSqlite()
            ? "NOCASE"
            : context.Database.IsNpgsql()
                ? postgresCollation
                : context.Database.IsSqlServer()
                    ? "Latin1_General_100_CI_AS"
                    : "utf8mb4_general_ci";

        var node = modelBuilder.Entity<ScopeAliasNode>();
        node.ToTable(table);
        node
            .Property(entity => entity.Id)
            .ValueGeneratedNever();
        node
            .Property(entity => entity.Scope)
            .HasMaxLength(40)
            .UseCollation(collation);
        node
            .Property(entity => entity.Name)
            .HasMaxLength(100);
        identity(node);
        node.HasNestedSet(nestedSet => nestedSet
            .HasNodeKey(entity => entity.Id)
            .HasScope(entity => entity.Scope)
            .HasTreeId(entity => entity.TreeId)
            .HasParent(entity => entity.ParentId)
            .HasBounds(entity => entity.Left, entity => entity.Right)
            .HasDepth(entity => entity.Depth)
            .HasPosition(entity => entity.Position)
            .OrderBy(entity => entity.Name));
    }
}

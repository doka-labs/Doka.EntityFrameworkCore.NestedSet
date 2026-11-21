namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps multiple ordered hierarchies to shared integer, string, and binary scope anchors.</summary>
public sealed class OrderingLockContext : NestedSetDbContext
{
    /// <summary>Creates the anchored test context with its real provider and optional observers.</summary>
    /// <param name="options">The provider and observer configuration.</param>
    public OrderingLockContext(
        DbContextOptions<OrderingLockContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        ConfigureAnchor<int>(modelBuilder, "OrderingIntegerAnchors");
        ConfigureAnchor<string>(modelBuilder, "OrderingStringAnchors");
        ConfigureAnchor<byte[]>(modelBuilder, "OrderingBinaryAnchors");
        ConfigureNode<int, FirstLockHierarchy>(modelBuilder, "OrderingFirstNodes");
        ConfigureNode<int, SecondLockHierarchy>(modelBuilder, "OrderingSecondNodes");
        ConfigureNode<string, FirstLockHierarchy>(modelBuilder, "OrderingStringNodes");
        ConfigureNode<byte[], FirstLockHierarchy>(modelBuilder, "OrderingBinaryNodes");
    }

    /// <summary>Maps a stable application-owned scope key with matching native storage semantics.</summary>
    private void ConfigureAnchor<TScope>(
        ModelBuilder modelBuilder,
        string table
    )
        where TScope : notnull
    {
        var anchor = modelBuilder.Entity<OrderingLockAnchor<TScope>>();
        anchor.ToTable(table);
        var key = anchor
            .Property(row => row.Id)
            .ValueGeneratedNever();

        ConfigureScope(key);
    }

    /// <summary>Maps one standalone closed CLR type to the anchor shared by its scope type.</summary>
    private void ConfigureNode<TScope, THierarchy>(
        ModelBuilder modelBuilder,
        string table
    )
        where TScope : notnull
    {
        var node = modelBuilder.Entity<OrderingLockNode<TScope, THierarchy>>();
        node.ToTable(table);
        node
            .Property(row => row.Id)
            .ValueGeneratedNever();

        node
            .Property(row => row.Name)
            .HasMaxLength(64);

        ConfigureScope(
            node
                .Property(row => row.Scope)
                .IsRequired());

        node.HasNestedSet(builder => builder
            .HasTreeId(row => row.TreeId)
            .HasScope(row => row.Scope)
            .HasParent(row => row.ParentId)
            .OrderBy(row => row.Name));
    }

    /// <summary>Preserves explicit string equality and identical bounded key types on anchors and nodes.</summary>
    private void ConfigureScope<TScope>(
        PropertyBuilder<TScope> property
    )
        where TScope : notnull
    {
        if (typeof(TScope) == typeof(string)
            || typeof(TScope) == typeof(byte[]))
        {
            property.HasMaxLength(64);
        }

        if (typeof(TScope) == typeof(string))
        {
            // WHY: Aliases must resolve through the same explicit collation on the anchor and hierarchy scope.
            var collation = Database.IsSqlite()
                ? "NOCASE"
                : Database.IsNpgsql()
                    ? "C"
                    : Database.IsSqlServer()
                        ? "Latin1_General_100_CI_AS"
                        : "utf8mb4_unicode_ci";

            property.UseCollation(collation);
        }
    }
}

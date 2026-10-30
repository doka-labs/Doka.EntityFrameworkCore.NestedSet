namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>Preserves PostgreSQL's explicit bounded character alias through actual model generation.</summary>
public sealed class UnicodeIdentityAliasCompiledContext : NestedSetDbContext
{
    /// <summary>Creates the separately cached alias model with its real PostgreSQL options.</summary>
    /// <param name="options">The provider and optional generated model.</param>
    public UnicodeIdentityAliasCompiledContext(
        DbContextOptions<UnicodeIdentityAliasCompiledContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<UnicodeIdentityCompiledNode>();
        node
            .Property(value => value.Id)
            .ValueGeneratedNever();
        node
            .Property(value => value.Scope)
            .HasMaxLength(8)
            .HasColumnType("bpchar(8)");
        node
            .Property(value => value.TreeId)
            .HasMaxLength(8)
            .HasColumnType("bpchar(8)");
        node.HasNestedSet(builder => builder
            .HasScope(value => value.Scope)
            .HasTreeId(value => value.TreeId)
            .HasParent(value => value.ParentId));
    }
}

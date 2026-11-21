namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>Provides real string identity mappings for provider-generated runtime models.</summary>
public sealed class UnicodeIdentityCompiledContext : NestedSetDbContext
{
    /// <summary>Creates a context with the exact provider or generated model selected by the test.</summary>
    /// <param name="options">The actual provider options and optional compiled model.</param>
    public UnicodeIdentityCompiledContext(
        DbContextOptions<UnicodeIdentityCompiledContext> options
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
            .HasMaxLength(8);

        node
            .Property(value => value.TreeId)
            .HasMaxLength(8);

        node.HasNestedSet(builder => builder
            .HasScope(value => value.Scope)
            .HasTreeId(value => value.TreeId)
            .HasParent(value => value.ParentId));
    }
}

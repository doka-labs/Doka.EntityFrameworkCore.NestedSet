using Doka.NestedSet;

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

/// <summary>Stores independent Scope and TreeId strings without adding a parent domain entity.</summary>
public sealed class UnicodeIdentityCompiledNode : INestedSetNode<int, string>
{
    /// <inheritdoc />
    public int Id { get; set; }

    /// <summary>Gets or sets the independent application partition.</summary>
    public string Scope { get; set; } = string.Empty;

    /// <inheritdoc />
    public string TreeId { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional direct parent identifier.</summary>
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

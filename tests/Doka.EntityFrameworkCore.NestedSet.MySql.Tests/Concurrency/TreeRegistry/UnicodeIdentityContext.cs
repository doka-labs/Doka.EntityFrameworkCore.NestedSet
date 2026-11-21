using Doka.EntityFrameworkCore.MySql;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Maps independent tables while sharing the exact same hierarchy contract.</summary>
/// <typeparam name="TIdentity">The model identity retained by the public facade.</typeparam>
internal abstract class UnicodeIdentityContext<TIdentity> : DbContext
    where TIdentity : notnull
{
    /// <summary>Uses the configured model and database connection.</summary>
    /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
    protected UnicodeIdentityContext(
        DbContextOptions options
    ) : base(options) { }

    /// <summary>Gets whether this model has an application partition.</summary>
    protected virtual bool Scoped => true;

    /// <summary>Maps the native type, conversion, and bound of an identity role.</summary>
    protected abstract void ConfigureIdentity(
        PropertyBuilder<TIdentity> property
    );

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.HasCharSet("utf8mb4");
        var node = modelBuilder.Entity<UnicodeIdentityNode<TIdentity>>();
        node.ToTable(
            GetType()
                .Name);

        node.HasKey(entity => entity.Id);
        node
            .Property(entity => entity.Id)
            .ValueGeneratedNever();

        node
            .Property(entity => entity.Name)
            .HasMaxLength(32);

        ConfigureIdentity(node.Property(entity => entity.TreeId));

        if (Scoped)
        {
            ConfigureIdentity(node.Property(entity => entity.Scope));
        }
        else
        {
            node.Ignore(entity => entity.Scope);
        }

        node.HasNestedSet(hierarchy =>
        {
            hierarchy
                .HasNodeKey(entity => entity.Id)
                .HasTreeId(entity => entity.TreeId)
                .HasParent(entity => entity.ParentId)
                .HasBounds(entity => entity.Left, entity => entity.Right)
                .HasDepth(entity => entity.Depth)
                .HasPosition(entity => entity.Position)
                .OrderBy(entity => entity.Name);

            if (Scoped)
            {
                hierarchy.HasScope(entity => entity.Scope);
            }
        });
    }
}

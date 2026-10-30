using System.Text;
using Doka.EntityFrameworkCore.MySql;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Maps independent tables while sharing the exact same hierarchy contract.</summary>
/// <typeparam name="TIdentity">The model identity retained by the public facade.</typeparam>
internal abstract class UnicodeIdentityContext<TIdentity>(DbContextOptions options) : DbContext(options)
    where TIdentity : notnull
{
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
        node.ToTable(GetType().Name);
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

/// <summary>Uses the native utf8mb4 character bound without a model converter.</summary>
internal sealed class UnicodeTextIdentityContext(DbContextOptions<UnicodeTextIdentityContext> options)
    : UnicodeIdentityContext<string>(options)
{
    /// <inheritdoc />
    protected override void ConfigureIdentity(
        PropertyBuilder<string> property
    ) => property
        .HasMaxLength(12)
        .UseCollation("utf8mb4_bin");
}

/// <summary>Uses the same native character bound after converting a model value object.</summary>
internal sealed class UnicodeConvertedIdentityContext(DbContextOptions<UnicodeConvertedIdentityContext> options)
    : UnicodeIdentityContext<UnicodeIdentityValue>(options)
{
    /// <inheritdoc />
    protected override void ConfigureIdentity(
        PropertyBuilder<UnicodeIdentityValue> property
    ) => property
        .HasConversion(value => value.Value, value => new UnicodeIdentityValue(value))
        .HasMaxLength(12)
        .UseCollation("utf8mb4_bin");
}

/// <summary>Uses a byte bound after converting text identities to their actual UTF-8 storage.</summary>
internal sealed class UnicodeBinaryIdentityContext(DbContextOptions<UnicodeBinaryIdentityContext> options)
    : UnicodeIdentityContext<string>(options)
{
    /// <inheritdoc />
    protected override void ConfigureIdentity(
        PropertyBuilder<string> property
    ) => property
        .HasConversion(value => Encoding.UTF8.GetBytes(value), value => Encoding.UTF8.GetString(value))
        .HasMaxLength(48)
        .HasColumnType("varbinary(48)");
}

/// <summary>Exercises the identical TreeId guard without an optional Scope mapping.</summary>
internal sealed class UnicodeScopelessIdentityContext(DbContextOptions<UnicodeScopelessIdentityContext> options)
    : UnicodeIdentityContext<string>(options)
{
    /// <inheritdoc />
    protected override bool Scoped => false;

    /// <inheritdoc />
    protected override void ConfigureIdentity(
        PropertyBuilder<string> property
    ) => property
        .HasMaxLength(12)
        .UseCollation("utf8mb4_bin");
}

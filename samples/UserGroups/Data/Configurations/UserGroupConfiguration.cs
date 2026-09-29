namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Maps an ordinary group entity and explicitly selects its nested-set properties.</summary>
public sealed class UserGroupConfiguration : IEntityTypeConfiguration<UserGroup>
{
    /// <inheritdoc />
    public void Configure(
        EntityTypeBuilder<UserGroup> builder
    )
    {
        builder.ToTable("UserGroups");
        builder.HasKey(group => group.Id);
        builder
            .Property(group => group.Name)
            .HasMaxLength(120)
            .IsRequired();

        builder.HasNestedSet(node => node
            .HasNodeKey(group => group.Id)
            .HasScope(group => group.TenantId)
            .HasTreeId(group => group.TreeId)
            .HasParent(group => group.ParentId)
            .HasBounds(group => group.Left, group => group.Right)
            .HasDepth(group => group.Depth)
            .HasPosition(group => group.Position));

        // WHY: Including TenantId in the relationship prevents a parent from another tenant at the database boundary.
        builder
            .HasOne<UserGroup>()
            .WithMany()
            .HasForeignKey(group => new
            {
                group.TenantId,
                group.ParentId,
            })
            .HasPrincipalKey(group => new
            {
                group.TenantId,
                group.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

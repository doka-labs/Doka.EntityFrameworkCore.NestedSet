namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Maps employees to a scoped supervisor tree.</summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <inheritdoc />
    public void Configure(
        EntityTypeBuilder<User> builder
    )
    {
        builder.ToTable("Users");
        builder.HasKey(user => user.Id);
        builder
            .Property(user => user.Name)
            .HasMaxLength(120)
            .IsRequired();

        builder
            .Property(user => user.Position)
            .HasMaxLength(120)
            .IsRequired();

        // WHY: Tree identity and coordinates are persistence mechanics; Position remains a business field on User.
        builder.Property<Guid>(UserHierarchyProperties.TreeId);
        builder.Property<long>(UserHierarchyProperties.Left);
        builder.Property<long>(UserHierarchyProperties.Right);
        builder.Property<long>(UserHierarchyProperties.NestedSetPosition);

        builder.HasNestedSet(node => node
            .HasNodeKey(user => user.Id)
            .HasScope(user => user.TenantId)
            .HasTreeId(UserHierarchyProperties.TreeId)
            .HasParent(user => user.SupervisorId)
            .HasBounds(UserHierarchyProperties.Left, UserHierarchyProperties.Right)
            .HasDepth(user => user.Depth)
            .HasPosition(UserHierarchyProperties.NestedSetPosition));

        // WHY: A supervisor's numeric key alone cannot prove that both employees belong to the same tenant.
        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(user => new
            {
                user.TenantId,
                user.SupervisorId,
            })
            .HasPrincipalKey(user => new
            {
                user.TenantId,
                user.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Maps role assignments whose endpoints must belong to the same tenant.</summary>
public sealed class UserGroupRoleConfiguration : IEntityTypeConfiguration<UserGroupRole>
{
    /// <inheritdoc />
    public void Configure(
        EntityTypeBuilder<UserGroupRole> builder
    )
    {
        builder.ToTable("UserGroupRoles");
        builder.HasKey(assignment => new
        {
            assignment.TenantId,
            assignment.UserGroupId,
            assignment.RoleId,
        });

        // WHY: Scope filtering in queries complements these foreign keys; it does not replace persisted integrity.
        builder
            .HasOne<UserGroup>()
            .WithMany()
            .HasForeignKey(assignment => new
            {
                assignment.TenantId,
                assignment.UserGroupId,
            })
            .HasPrincipalKey(group => new
            {
                group.TenantId,
                group.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne<Role>()
            .WithMany()
            .HasForeignKey(assignment => new
            {
                assignment.TenantId,
                assignment.RoleId,
            })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

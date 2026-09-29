namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Maps grants whose role and permission definition share the tenant key.</summary>
public sealed class RolePrivilegeConfiguration : IEntityTypeConfiguration<RolePrivilege>
{
    /// <inheritdoc />
    public void Configure(
        EntityTypeBuilder<RolePrivilege> builder
    )
    {
        builder.ToTable("RolePrivileges");
        builder.HasKey(grant => new
        {
            grant.TenantId,
            grant.RoleId,
            grant.PrivilegeCode,
        });

        builder
            .Property(grant => grant.PrivilegeCode)
            .HasMaxLength(80);

        builder
            .HasOne<Role>()
            .WithMany()
            .HasForeignKey(grant => new
            {
                grant.TenantId,
                grant.RoleId,
            })
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne<Privilege>()
            .WithMany()
            .HasForeignKey(grant => new
            {
                grant.TenantId,
                grant.PrivilegeCode,
            })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

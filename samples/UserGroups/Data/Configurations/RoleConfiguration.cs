namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Maps roles with a tenant-qualified identity used by grant relationships.</summary>
public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    /// <inheritdoc />
    public void Configure(
        EntityTypeBuilder<Role> builder
    )
    {
        builder.ToTable("Roles");
        builder.HasKey(role => new
        {
            role.TenantId,
            role.Id,
        });

        builder
            .Property(role => role.Id)
            .ValueGeneratedNever();

        builder
            .Property(role => role.Name)
            .HasMaxLength(120)
            .IsRequired();
    }
}

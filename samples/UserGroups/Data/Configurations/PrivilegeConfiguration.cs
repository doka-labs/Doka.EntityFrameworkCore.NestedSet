namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Maps permission codes inside their tenant partition.</summary>
public sealed class PrivilegeConfiguration : IEntityTypeConfiguration<Privilege>
{
    /// <inheritdoc />
    public void Configure(
        EntityTypeBuilder<Privilege> builder
    )
    {
        builder.ToTable("Privileges");
        builder.HasKey(privilege => new
        {
            privilege.TenantId,
            privilege.Code,
        });

        builder
            .Property(privilege => privilege.Code)
            .HasMaxLength(80);
    }
}

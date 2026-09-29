namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Maps tenant-safe many-to-many membership between users and application groups.</summary>
public sealed class UserGroupMembershipConfiguration : IEntityTypeConfiguration<UserGroupMembership>
{
    /// <inheritdoc />
    public void Configure(
        EntityTypeBuilder<UserGroupMembership> builder
    )
    {
        builder.ToTable("UserGroupMemberships");

        // ReSharper disable once SimilarAnonymousTypeNearby
        builder.HasKey(membership => new
        {
            membership.TenantId,
            membership.UserId,
            membership.UserGroupId,
        });

        // WHY: The primary key serves user-to-group reads; this reverse index serves group-to-user reads.
        // ReSharper disable once SimilarAnonymousTypeNearby
        builder.HasIndex(membership => new
        {
            membership.TenantId,
            membership.UserGroupId,
            membership.UserId,
        });

        // WHY: The tenant key in both FKs prevents links between endpoints from different tenants.
        // Database cascade also removes links when a hierarchy deletion physically deletes a user.
        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(membership => new
            {
                membership.TenantId,
                membership.UserId,
            })
            .HasPrincipalKey(user => new
            {
                user.TenantId,
                user.Id,
            })
            .OnDelete(DeleteBehavior.Cascade);

        // WHY: Group deletion remains explicit because direct roles and membership require policy decisions.
        builder
            .HasOne<UserGroup>()
            .WithMany()
            .HasForeignKey(membership => new
            {
                membership.TenantId,
                membership.UserGroupId,
            })
            .HasPrincipalKey(group => new
            {
                group.TenantId,
                group.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

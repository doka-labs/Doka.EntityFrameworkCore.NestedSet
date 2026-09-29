namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Composes nested-set saves with an application's existing EF context and timestamp customization.</summary>
public class UserGroupContext : DbContext
{
    /// <summary>Creates a context using options supplied by the sample or a design-time factory.</summary>
    /// <param name="options">The provider and connection options.</param>
    public UserGroupContext(
        DbContextOptions options
    ) : base(options) { }

    /// <summary>Gets the ordinary EF set used for tracked group payload changes.</summary>
    public DbSet<UserGroup> Groups => Set<UserGroup>();

    /// <summary>Gets the ordinary EF set of users whose supervisor structure is mapped as a nested set.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>Gets the tenant-owned links between users and their application groups.</summary>
    public DbSet<UserGroupMembership> UserGroupMemberships => Set<UserGroupMembership>();

    /// <summary>Gets tenant-owned application roles.</summary>
    public DbSet<Role> Roles => Set<Role>();

    /// <summary>Gets tenant-owned permission definitions.</summary>
    public DbSet<Privilege> Privileges => Set<Privilege>();

    /// <summary>Gets direct group-to-role assignments.</summary>
    public DbSet<UserGroupRole> GroupRoles => Set<UserGroupRole>();

    /// <summary>Gets application grants supplied by roles.</summary>
    public DbSet<RolePrivilege> RolePrivileges => Set<RolePrivilege>();

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        // WHY: Persist the sample's comparison policy in migrations so database recreation keeps it after --reset.
        if (Database.ProviderName == "Doka.EntityFrameworkCore.MySql")
        {
            modelBuilder.UseCollation("utf8mb4_bin");
        }

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(UserGroupContext).Assembly);
    }

    /// <summary>Saves payloads and coordinated hierarchy changes using the existing context base class.</summary>
    /// <param name="acceptAllChangesOnSuccess">Whether to accept changes after the complete save succeeds.</param>
    /// <param name="cancellationToken">The token used for locking, payload persistence, and structural updates.</param>
    /// <returns>The number of EF entries saved, excluding set-based hierarchy updates.</returns>
    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        StampUpdatedPayloads();

        // WHY: Tracker acceptance must wait until both the application's save and the hierarchy work succeed.
        return this.SaveNestedSetChangesAsync(
            acceptAllChangesOnSuccess,
            token => base.SaveChangesAsync(false, token),
            cancellationToken);
    }

    /// <summary>Saves synchronously only when the changes do not require asynchronous hierarchy coordination.</summary>
    /// <returns>The number of EF entries saved.</returns>
    public override int SaveChanges() => SaveChanges(true);

    /// <summary>Preserves the synchronous EF API while guarding changes that need hierarchy coordination.</summary>
    /// <param name="acceptAllChangesOnSuccess">Whether to accept successfully saved tracker changes.</param>
    /// <returns>The number of EF entries saved.</returns>
    public override int SaveChanges(
        bool acceptAllChangesOnSuccess
    )
    {
        StampUpdatedPayloads();

        // ReSharper disable once MethodHasAsyncOverload
        // WHY: This override is the explicitly synchronous compatibility path; it never blocks an asynchronous save.
        return this.SaveNestedSetChanges(acceptAllChangesOnSuccess, () => base.SaveChanges(false));
    }

    /// <summary>Applies the application's existing UTC timestamp policy before entering the save boundary.</summary>
    private void StampUpdatedPayloads()
    {
        var timestamp = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<UserGroup>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = timestamp;
            }
        }

        foreach (var entry in ChangeTracker.Entries<Role>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = timestamp;
            }
        }
    }
}

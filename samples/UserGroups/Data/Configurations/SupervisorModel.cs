namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Maps the application's role, routing, visibility, and validity data beside both hierarchies.</summary>
internal static class SupervisorModel
{
    /// <summary>Configures ordinary relational constraints and the sample's exact ASCII routing comparison.</summary>
    /// <param name="model">The application's model builder.</param>
    /// <param name="provider">The relational provider selecting an equivalent binary comparison.</param>
    internal static void Configure(
        ModelBuilder model,
        string? provider
    )
    {
        model
            .Entity<UserGroup>()
            .HasQueryFilter(row => row.Visible);
        model
            .Entity<User>()
            .HasQueryFilter(row => row.Visible);
        model
            .Entity<Role>()
            .HasQueryFilter(row => row.Visible);
        model
            .Entity<UserGroupMembership>()
            .HasQueryFilter(row => row.Visible);
        model
            .Entity<MembershipRole>()
            .HasQueryFilter(row => row.Visible);
        model
            .Entity<UserRole>()
            .HasQueryFilter(row => row.Visible);
        model
            .Entity<UserGroup>()
            .Property(row => row.Visible)
            .HasDefaultValue(true);
        model
            .Entity<User>()
            .Property(row => row.Visible)
            .HasDefaultValue(true);
        model
            .Entity<Role>()
            .Property(row => row.Visible)
            .HasDefaultValue(true);
        model
            .Entity<UserGroupMembership>()
            .Property(row => row.Visible)
            .HasDefaultValue(true);
        model
            .Entity<MembershipRole>()
            .Property(row => row.Visible)
            .HasDefaultValue(true);
        model
            .Entity<UserRole>()
            .Property(row => row.Visible)
            .HasDefaultValue(true);
        model
            .Entity<GroupType>()
            .HasKey(row => new
            {
                row.TenantId,
                row.Id,
            });
        model
            .Entity<GroupType>()
            .Property(row => row.Id)
            .ValueGeneratedNever();
        model
            .Entity<GroupType>()
            .Property(row => row.Name)
            .HasMaxLength(80);
        model
            .Entity<UserGroup>()
            .HasOne<GroupType>()
            .WithMany()
            .HasForeignKey(row => new
            {
                row.TenantId,
                row.TypeId,
            })
            .HasPrincipalKey(row => new
            {
                row.TenantId,
                row.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);

        var local = model.Entity<MembershipRole>();
        local.HasKey(row => new
        {
            row.TenantId,
            row.UserId,
            row.UserGroupId,
            row.RoleId,
        });
        local
            .HasOne<UserGroupMembership>()
            .WithMany()
            .HasForeignKey(row => new
            {
                row.TenantId,
                row.UserId,
                row.UserGroupId
            })
            .OnDelete(DeleteBehavior.Cascade);
        local
            .HasOne<Role>()
            .WithMany()
            .HasForeignKey(row => new
            {
                row.TenantId,
                row.RoleId
            })
            .OnDelete(DeleteBehavior.Restrict);

        var global = model.Entity<UserRole>();
        global.HasKey(row => new
        {
            row.TenantId,
            row.UserId,
            row.RoleId
        });
        global
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(row => new
            {
                row.TenantId,
                row.UserId
            })
            .HasPrincipalKey(row => new
            {
                row.TenantId,
                row.Id
            })
            .OnDelete(DeleteBehavior.Cascade);

        global
            .HasOne<Role>()
            .WithMany()
            .HasForeignKey(row => new
            {
                row.TenantId,
                row.RoleId
            })
            .OnDelete(DeleteBehavior.Restrict);

        var periods = model.Entity<GroupRelationPeriod>();
        periods.HasKey(row => new
        {
            row.TenantId,
            row.ParentId,
            row.ChildId,
            row.FromDate,
        });
        periods
            .HasOne<UserGroup>()
            .WithMany()
            .HasForeignKey(row => new
            {
                row.TenantId,
                Id = row.ParentId
            })
            .HasPrincipalKey(row => new
            {
                row.TenantId,
                row.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);

        periods
            .HasOne<UserGroup>()
            .WithMany()
            .HasForeignKey(row => new
            {
                row.TenantId,
                Id = row.ChildId
            })
            .HasPrincipalKey(row => new
            {
                row.TenantId,
                row.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);

        var collation = provider switch
        {
            "Doka.EntityFrameworkCore.MySql" => "utf8mb4_bin",
            "Npgsql.EntityFrameworkCore.PostgreSQL" => "C",
            "Microsoft.EntityFrameworkCore.SqlServer" => "Latin1_General_100_BIN2",
            _ => "BINARY",
        };

        ConfigureMetadata<UserMetadata, UserMetadataDefinition>(model, false, false, collation);
        ConfigureMetadata<MembershipMetadata, MembershipMetadataDefinition>(model, true, true, collation);
        ConfigureMetadata<GroupMetadata, GroupMetadataDefinition>(model, true, false, collation);
    }

    /// <summary>Keeps each scalar owner and its permitted typed definition protected by foreign keys.</summary>
    private static void ConfigureMetadata<TValue, TDefinition>(
        ModelBuilder model,
        bool group,
        bool membership,
        string collation
    )
        where TValue : class
        where TDefinition : class
    {
        // WHY: Three concrete tables retain real owner foreign keys instead of an unchecked polymorphic object key.
        var value = model.Entity<TValue>();
        var definition = model.Entity<TDefinition>();
        string[] definitionKey = group ? ["TenantId", "GroupTypeId", "FieldCode"] : ["TenantId", "FieldCode"];

        string[] ownerKey = group
            ? membership ? ["TenantId", "UserId", "UserGroupId", "FieldCode"] : ["TenantId", "UserGroupId", "FieldCode"]
            : ["TenantId", "UserId", "FieldCode"];

        definition.HasKey(definitionKey);
        definition
            .Property<string>("FieldCode")
            .HasMaxLength(64)
            .UseCollation(collation);
        definition
            .Property<string>("ValueKind")
            .HasMaxLength(16);
        value.HasKey(ownerKey);
        value
            .Property<string>("FieldCode")
            .HasMaxLength(64)
            .UseCollation(collation);
        value
            .Property<string?>("Value")
            .HasMaxLength(120)
            .UseCollation(collation);
        value
            .HasOne<TDefinition>()
            .WithMany()
            .HasForeignKey(definitionKey)
            .OnDelete(DeleteBehavior.Restrict);

        if (group)
        {
            definition
                .HasOne<GroupType>()
                .WithMany()
                .HasForeignKey("TenantId", "GroupTypeId")
                .OnDelete(DeleteBehavior.Restrict);
        }

        if (membership)
        {
            value
                .HasOne<UserGroupMembership>()
                .WithMany()
                .HasForeignKey("TenantId", "UserId", "UserGroupId")
                .OnDelete(DeleteBehavior.Cascade);
        }
        else if (group)
        {
            value
                .HasOne<UserGroup>()
                .WithMany()
                .HasForeignKey("TenantId", "UserGroupId")
                .HasPrincipalKey("TenantId", "Id")
                .OnDelete(DeleteBehavior.Cascade);
        }
        else
        {
            value
                .HasOne<User>()
                .WithMany()
                .HasForeignKey("TenantId", "UserId")
                .HasPrincipalKey("TenantId", "Id")
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}

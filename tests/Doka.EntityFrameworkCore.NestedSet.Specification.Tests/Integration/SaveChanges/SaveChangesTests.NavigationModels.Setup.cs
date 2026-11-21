namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Creates an isolated navigation model through the existing real-provider database lifecycle.</summary>
    private async Task<TestDatabase> CreateNavigationDatabaseAsync(
        CancellationToken cancellationToken
    )
    {
        var database = await TestDatabase.CreateAsync(Engine, cancellationToken: cancellationToken);

        try
        {
            await using var context = await CreateNavigationContextAsync(
                database,
                wrapped: true,
                cancellationToken: cancellationToken);

            await context
                .GetService<IRelationalDatabaseCreator>()
                .CreateTablesAsync(cancellationToken);

            var owner = new NavigationScopeOwner { Value = "owner" };
            await context.AddRangeAsync(
                [owner, new NavigationScopeOwner { Value = "other owner" }, new NavigationNote { Id = 1 }],
                cancellationToken);

            await context.SaveChangesAsync(cancellationToken);
            var hierarchy = context
                .NestedSet<NavigationNode>()
                .ForScope(owner.Id);

            await hierarchy.InsertRootAsync(
                new NavigationNode
                {
                    Id = 1,
                    Name = "Root",
                },
                s_treeId,
                cancellationToken);

            await hierarchy.InsertChildAsync(
                new NavigationNode
                {
                    Id = 2,
                    Name = "Alpha",
                },
                1,
                cancellationToken);

            await hierarchy.InsertChildAsync(
                new NavigationNode
                {
                    Id = 3,
                    Name = "Bravo",
                },
                1,
                cancellationToken);

            return database;
        }
        catch
        {
            // WHY: The caller can only own the database after all model tables and seed rows exist.
            await database.DisposeAsync();

            throw;
        }
    }

    /// <summary>Reuses the selected provider while choosing its coordinated or ordinary save boundary.</summary>
    /// <param name="cancellationToken">The caller's token for setup, queries and verification.</param>
    /// <param name="database">The isolated database owned by this test.</param>
    /// <param name="wrapped">Whether to use NestedSetDbContext's save overrides.</param>
    /// <param name="interceptors">Observers scoped to this context.</param>
    /// <returns>A context whose lifetime belongs to the caller.</returns>
    private static async Task<DbContext> CreateNavigationContextAsync(
        TestDatabase database,
        bool wrapped,
        CancellationToken cancellationToken,
        params IInterceptor[] interceptors
    )
    {
        var options = await SaveSemanticsFixture.OptionsAsync(
            database,
            cancellationToken: cancellationToken,
            interceptors);

        return wrapped
            ? new NavigationSaveContext(options)
            : new NavigationPlainContext(options);
    }

    /// <summary>Maps parent and scope navigations independently of the scalar-only ordering fixture.</summary>
    private static void ConfigureNavigationModel(
        ModelBuilder builder
    )
    {
        var node = builder.Entity<NavigationNode>();
        node.ToTable("NavigationSaveNodes");
        node
            .Property(value => value.Id)
            .ValueGeneratedNever();

        node
            .Property(value => value.Name)
            .HasMaxLength(128);

        node.HasNestedSet(value => value
            .HasTreeId(item => item.TreeId)
            .HasScope(item => item.Scope)
            .HasParent(item => item.ParentId)
            .OrderBy(item => item.Name));

        node
            .HasOne(value => value.Parent)
            .WithMany(value => value.Children)
            .HasForeignKey(value => new
            {
                value.Scope,
                value.ParentId,
            })
            .HasPrincipalKey(value => new
            {
                value.Scope,
                value.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);

        node
            .HasOne(value => value.ScopeOwner)
            .WithMany(value => value.Nodes)
            .HasForeignKey(value => value.Scope)
            .OnDelete(DeleteBehavior.Cascade);

        node
            .HasOne(value => value.PayloadOwner)
            .WithMany(value => value.Nodes)
            .HasForeignKey(value => new
            {
                value.Scope,
                value.PayloadOwnerId,
            })
            .HasPrincipalKey(value => new
            {
                value.Scope,
                value.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);

        var owner = builder.Entity<NavigationScopeOwner>();
        owner.ToTable("NavigationSaveOwners");
        owner
            .Property(value => value.Value)
            .HasMaxLength(128);

        var payloadOwner = builder.Entity<NavigationPayloadOwner>();
        payloadOwner.ToTable("NavigationSavePayloadOwners");
        payloadOwner.HasKey(value => new
        {
            value.Scope,
            value.Id,
        });

        payloadOwner
            .Property(value => value.Id)
            .ValueGeneratedNever();

        var note = builder.Entity<NavigationNote>();
        note.ToTable("NavigationSaveNotes");
        note
            .Property(value => value.Id)
            .ValueGeneratedNever();

        note
            .Property(value => value.Value)
            .HasMaxLength(128);
    }
}

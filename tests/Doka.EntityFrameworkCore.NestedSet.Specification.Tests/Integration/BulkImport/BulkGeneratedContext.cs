namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps immediate nullable parent FKs without navigations and generated keys and sort defaults.</summary>
public sealed class BulkGeneratedContext : NestedSetDbContext
{
    /// <summary>Creates a context using fixture-owned database options.</summary>
    public BulkGeneratedContext(
        DbContextOptions<BulkGeneratedContext> options
    ) : base(options) { }

    /// <summary>Gets or sets a test callback that inspects the tracker after the base save returns.</summary>
    public Action<DbContext>? AfterBaseSave { get; set; }

    /// <inheritdoc />
    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        var saved = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        AfterBaseSave?.Invoke(this);

        return saved;
    }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<BulkGeneratedNode>();
        node.ToTable("BulkGeneratedNodes");
        node
            .Property(value => value.Name)
            .HasMaxLength(80)
            .HasDefaultValue("Generated");

        var details = node.ComplexProperty(value => value.Details);
        details
            .Property(value => value.Label)
            .HasMaxLength(80)
            .HasDefaultValue("Generated detail");

        if (Database.IsSqlite())
        {
            // WHY: SQLite RETURNING precedes AFTER triggers; EF must select the final generated token after the update.
            node.ToTable(table => table.UseSqlReturningClause(false));
            node
                .Property(value => value.Version)
                .HasDefaultValue(0)
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();

            details
                .Property(value => value.Revision)
                .HasDefaultValue(0)
                .ValueGeneratedOnAddOrUpdate();

            node
                .ComplexCollection(value => value.History)
                .ToJson();
        }
        else
        {
            node.Ignore(value => value.Version);
            node.Ignore(value => value.History);
        }

        node.HasNestedSet(builder => builder
            .HasTreeId(value => value.TreeId)
            .HasScope(value => value.Scope)
            .HasParent(value => value.ParentId)
            .OrderBy(value => value.Name, NullSortOrder.Last));

        var manual = modelBuilder.Entity<BulkManualNode>();
        manual.HasNestedSet(builder => builder
            .HasTreeId(value => value.TreeId)
            .HasScope(value => value.Scope)
            .HasParent(value => value.ParentId));

        var navigation = modelBuilder.Entity<BulkNavigationNode>();
        navigation
            .Property(value => value.Id)
            .ValueGeneratedNever();

        navigation
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

        navigation.HasNestedSet(builder => builder
            .HasTreeId(value => value.TreeId)
            .HasScope(value => value.Scope)
            .HasParent(value => value.ParentId));
    }
}

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps the custom-property and optional-interface entities used by relational scenarios.</summary>
public sealed class TreeContext : DbContext
{
    /// <summary>Creates an integration-test context using the configured relational provider.</summary>
    /// <param name="options">The provider and interceptor configuration.</param>
    public TreeContext(
        DbContextOptions<TreeContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        if (Database.IsNpgsql())
        {
            // WHY: A nondefault schema verifies that internal lock SQL honors relational table mapping.
            modelBuilder.HasDefaultSchema("hierarchies");
        }

        var node = modelBuilder.Entity<TreeNode>();
        node.HasKey(x => x.NodeId);
        node
            .Property(x => x.NodeId)
            .ValueGeneratedNever();

        node.HasNestedSet(x => x
            .HasNodeKey(n => n.NodeId)
            .HasBounds(n => n.Start, n => n.End)
            .HasDepth(n => n.Depth)
            .HasTreeId(n => n.TreeId)
            .HasScope(n => n.Tree)
            .HasParent(n => n.Parent)
            .HasPosition(n => n.Position));

        var concurrent = modelBuilder.Entity<ConcurrentNode>();
        concurrent
            .Property(x => x.Id)
            .ValueGeneratedNever();

        concurrent.HasNestedSet(x => x
            .HasTreeId(n => n.TreeId)
            .HasScope(n => n.Tree)
            .HasParent(n => n.ParentId));

        var unscoped = modelBuilder.Entity<UnscopedQueryNode>();
        unscoped
            .Property(x => x.Id)
            .ValueGeneratedNever();

        unscoped.HasQueryFilter(node => node.Visible);
        unscoped.HasNestedSet(nestedSet => nestedSet
            .HasNodeKey(node => node.Id)
            .HasTreeId(node => node.TreeId)
            .HasParent(node => node.ParentId)
            .HasBounds(node => node.Left, node => node.Right)
            .HasDepth(node => node.Depth)
            .HasPosition(node => node.Position));

        modelBuilder
            .Entity<UnrelatedRow>()
            .Property(x => x.Id)
            .ValueGeneratedNever();

        modelBuilder.Entity<GeneratedAuditRow>();
        var binary = modelBuilder.Entity<BinaryNode>();
        binary
            .Property(x => x.Id)
            .HasMaxLength(64)
            .ValueGeneratedNever();

        binary
            .Property(x => x.ParentId)
            .HasMaxLength(64);

        binary.HasNestedSet(x => x
            .HasTreeId(n => n.TreeId)
            .HasScope(n => n.Tree)
            .HasParent(n => n.ParentId));

        var constrained = modelBuilder.Entity<ConstrainedNode>();
        constrained
            .Property(x => x.Id)
            .ValueGeneratedNever();

        constrained
            .HasOne<ConstrainedNode>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.Tree,
                x.ParentId,
            })
            .HasPrincipalKey(x => new
            {
                x.Tree,
                x.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);

        constrained.HasNestedSet(x => x
            .HasTreeId(n => n.TreeId)
            .HasScope(n => n.Tree)
            .HasParent(n => n.ParentId));

        var guid = modelBuilder.Entity<GuidNode>();
        guid
            .Property(x => x.Id)
            .ValueGeneratedNever();

        guid.HasNestedSet(x => x
            .HasTreeId(n => n.TreeId)
            .HasScope(n => n.Tree)
            .HasParent(n => n.ParentId));

        var text = modelBuilder.Entity<TextNode>();
        text
            .Property(x => x.Id)
            .HasMaxLength(64)
            .ValueGeneratedNever();

        text
            .Property(x => x.ParentId)
            .HasMaxLength(64);

        text
            .Property(x => x.Tree)
            .HasMaxLength(64);

        if (Database.IsSqlite())
        {
            // WHY: Deliberately leave ParentId binary: lookups must use the principal key collation.
            text
                .Property(x => x.Id)
                .UseCollation("NOCASE");
        }

        text.HasNestedSet(x => x
            .HasTreeId(n => n.TreeId)
            .HasScope(n => n.Tree)
            .HasParent(n => n.ParentId));

        if (Database.IsNpgsql())
        {
            // WHY: Lowercase lock columns exercise mapped names instead of relying on CLR-name SQL.
            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entity.GetProperties())
                {
                    property.SetColumnName(property.Name.ToLowerInvariant());
                }
            }
        }
    }
}

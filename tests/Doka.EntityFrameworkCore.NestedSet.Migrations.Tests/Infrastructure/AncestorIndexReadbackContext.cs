namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>Maps actual assigned-key imports without changing the historical migration model.</summary>
internal sealed class AncestorIndexReadbackContext : NestedSetDbContext
{
    /// <summary>The physical table whose native insert commands are observed.</summary>
    internal const string TableName = "AncestorReadbackRows";

    /// <summary>The generated shadow-property name used for the optional ancestor access path.</summary>
    internal const string FlagProperty = "IsBranch";

    /// <summary>The declared native trigger that excludes MySQL's known-key readback optimization.</summary>
    internal const string TriggerName = "AncestorReadbackBeforeInsert";

    /// <summary>Creates one isolated ordinary or declared-trigger model.</summary>
    /// <param name="options">The actual Doka provider and the readback observers.</param>
    /// <param name="declaredTrigger">Whether the physical insert trigger is part of the EF model.</param>
    internal AncestorIndexReadbackContext(
        DbContextOptions<AncestorIndexReadbackContext> options,
        bool declaredTrigger
    ) : base(options)
    {
        DeclaredTrigger = declaredTrigger;
    }

    /// <summary>Gets whether the safe declared-trigger insertion path is required.</summary>
    internal bool DeclaredTrigger { get; }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<AncestorIndexReadbackNode>();
        node.ToTable(
            TableName,
            table =>
            {
                if (DeclaredTrigger)
                {
                    table.HasTrigger(TriggerName);
                }
            });

        node.HasKey(row => row.Id);
        node
            .Property(row => row.Id)
            .ValueGeneratedNever();
        node
            .Property(row => row.Stamp)
            .HasComputedColumnSql("`Input` * 17 + 3", stored: true);
        node.HasNestedSet(builder => builder
            .HasNodeKey(row => row.Id)
            .HasScope(row => row.Scope)
            .HasTreeId(row => row.TreeId)
            .HasParent(row => row.ParentId)
            .HasBounds(row => row.Left, row => row.Right)
            .HasDepth(row => row.Depth)
            .HasPosition(row => row.Position)
            .HasAncestorIndex(FlagProperty));
    }
}

/// <summary>Prevents declared-trigger metadata from leaking into the ordinary readback model.</summary>
public sealed class AncestorIndexReadbackModelCacheKeyFactory : IModelCacheKeyFactory
{
    /// <inheritdoc />
    public object Create(
        DbContext context,
        bool designTime
    )
    {
        var readback = (AncestorIndexReadbackContext)context;

        return (context.GetType(), readback.DeclaredTrigger, designTime);
    }
}

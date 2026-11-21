namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps ordering-specific domain data through the ordinary asynchronous save integration.</summary>
public abstract class OrderingContext : NestedSetDbContext
{
    /// <summary>Creates a context using the provider selected by its fixture.</summary>
    protected OrderingContext(
        DbContextOptions options
    ) : base(options) { }

    /// <summary>Gets the distinct prefix preventing cross-model table and lock collisions.</summary>
    protected abstract string TablePrefix { get; }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    ) => ConfigureModel(modelBuilder, TablePrefix);

    /// <summary>Shares the exact table mapping with the deliberately unintegrated negative-control context.</summary>
    internal static void ConfigureModel(
        ModelBuilder modelBuilder,
        string tablePrefix
    )
    {
        var node = modelBuilder.Entity<OrderingNode>();
        node.ToTable(tablePrefix + "OrderingNodes");
        node
            .Property(value => value.Id)
            .ValueGeneratedNever();

        node
            .Property(value => value.Name)
            .HasMaxLength(128);

        node
            .Property(value => value.Payload)
            .HasMaxLength(256);

        node
            .Property(value => value.Category)
            .HasConversion<string>()
            .HasMaxLength(32);

        node.HasNestedSet(builder =>
        {
            builder
                .HasTreeId(value => value.TreeId)
                .HasScope(value => value.Scope)
                .HasParent(value => value.ParentId);

            if (tablePrefix == "Converted")
            {
                // WHY: Alphabetic provider values differ from enum ordinals and expose CLR-side ordering.
                builder
                    .OrderBy(value => value.Category)
                    .ThenBy(value => value.Name, NullSortOrder.Last);
            }
            else
            {
                builder
                    .OrderBy(value => value.Name, NullSortOrder.Last)
                    .ThenByDescending(value => value.Priority);
            }

            if (tablePrefix == "Flexible")
            {
                builder.HasOrderMode(NestedSetOrderMode.AllowManualPlacement);
            }
        });

        var marker = modelBuilder.Entity<OrderingMarker>();
        marker.ToTable(tablePrefix + "OrderingMarkers");
        marker
            .Property(value => value.Id)
            .ValueGeneratedNever();

        marker
            .Property(value => value.Value)
            .HasMaxLength(128);
    }
}

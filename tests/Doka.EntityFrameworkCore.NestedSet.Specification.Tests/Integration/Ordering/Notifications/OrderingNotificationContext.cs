namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Uses notification tracking and independent tables in an existing relational test database.</summary>
public sealed class OrderingNotificationContext : NestedSetDbContext
{
    /// <summary>Creates the context against the database owned by the test.</summary>
    /// <param name="options">The provider connection and optional fault injection.</param>
    public OrderingNotificationContext(
        DbContextOptions options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<OrderingNotificationNode>();
        node.ToTable("NotificationOrderingNodes");
        node.HasChangeTrackingStrategy(ChangeTrackingStrategy.ChangingAndChangedNotifications);
        node
            .Property(value => value.Id)
            .ValueGeneratedNever();

        node.HasNestedSet(builder => builder
            .HasTreeId(value => value.TreeId)
            .HasScope(value => value.Scope)
            .HasParent(value => value.ParentId)
            .OrderBy(value => value.Name));

        var payload = modelBuilder.Entity<OrderingNotificationPayload>();
        payload.ToTable("NotificationOrderingPayloads");
        payload.HasChangeTrackingStrategy(ChangeTrackingStrategy.ChangingAndChangedNotifications);
        payload
            .Property(value => value.Id)
            .ValueGeneratedNever();
    }
}

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps the same converted keys through automatic sibling ordering.</summary>
internal sealed class OrderedBroadNodeKeyContext : DbContext
{
    /// <summary>Creates a sorted context for the model-compatibility database.</summary>
    internal OrderedBroadNodeKeyContext(
        DbContextOptions<OrderedBroadNodeKeyContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    ) => BroadNodeKeyModel.Configure(modelBuilder, this, "OrderedBroadNodeKeyNodes", ordered: true);
}

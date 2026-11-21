namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps domain-equal key aliases to distinct database identities.</summary>
internal sealed class BroadNodeKeyContext : DbContext
{
    /// <summary>Creates a context for the model-compatibility database.</summary>
    internal BroadNodeKeyContext(
        DbContextOptions<BroadNodeKeyContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    ) => BroadNodeKeyModel.Configure(modelBuilder, this, "BroadNodeKeyNodes", ordered: false);
}

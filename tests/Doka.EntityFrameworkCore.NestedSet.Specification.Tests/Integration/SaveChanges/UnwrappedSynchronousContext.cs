namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>
///     Exposes an inherited synchronous save that intentionally omits the documented custom-base wrapper.
/// </summary>
public sealed class UnwrappedSynchronousContext : DbContext
{
    /// <summary>Creates a negative control without supplying the synchronous save override.</summary>
    /// <param name="options">The established provider configuration with context-neutral option metadata.</param>
    public UnwrappedSynchronousContext(
        DbContextOptions options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    ) => OrderingContext.ConfigureModel(modelBuilder, "Strict");
}

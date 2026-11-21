namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Uses a typed public constructor so the same model can be leased through EF context pooling.</summary>
public sealed class SaveBoundaryContext : OrderingContext
{
    /// <summary>Creates a context with the fixture's real provider and test-specific observers.</summary>
    /// <param name="options">The immutable options also accepted by the pooled context factory.</param>
    public SaveBoundaryContext(
        DbContextOptions<SaveBoundaryContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override string TablePrefix => "Strict";
}

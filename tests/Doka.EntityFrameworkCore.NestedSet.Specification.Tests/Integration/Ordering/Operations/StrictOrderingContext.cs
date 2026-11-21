namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Uses the default strict ordering mode and an independent EF model cache entry.</summary>
public sealed class StrictOrderingContext : OrderingContext
{
    /// <summary>Creates the strict test context.</summary>
    public StrictOrderingContext(
        DbContextOptions options
    ) : base(options) { }

    /// <inheritdoc />
    protected override string TablePrefix => "Strict";
}

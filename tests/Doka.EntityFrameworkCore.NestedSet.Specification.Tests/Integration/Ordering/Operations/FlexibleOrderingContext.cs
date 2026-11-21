namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Allows explicit placement while retaining automatic placement for ordinary inserts and renames.</summary>
public sealed class FlexibleOrderingContext : OrderingContext
{
    /// <summary>Creates the flexible test context.</summary>
    public FlexibleOrderingContext(
        DbContextOptions options
    ) : base(options) { }

    /// <inheritdoc />
    protected override string TablePrefix => "Flexible";
}

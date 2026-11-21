namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Orders a converted domain value through its native persisted representation.</summary>
public sealed class ConvertedOrderingContext : OrderingContext
{
    /// <summary>Creates the converted-sort test context.</summary>
    public ConvertedOrderingContext(
        DbContextOptions options
    ) : base(options) { }

    /// <inheritdoc />
    protected override string TablePrefix => "Converted";
}

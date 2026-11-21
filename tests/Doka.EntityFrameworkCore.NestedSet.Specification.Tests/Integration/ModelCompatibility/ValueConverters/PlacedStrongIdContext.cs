namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Allows manual placement, so changed nodes are placed one at a time.</summary>
internal sealed class PlacedStrongIdContext : OrderedStrongIdContext
{
    /// <summary>Creates the manual-placement converted-key context.</summary>
    internal PlacedStrongIdContext(
        DbContextOptions<PlacedStrongIdContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override NestedSetOrderMode Mode => NestedSetOrderMode.AllowManualPlacement;

    /// <inheritdoc />
    protected override string Table => "PlacedStrongIdNodes";
}

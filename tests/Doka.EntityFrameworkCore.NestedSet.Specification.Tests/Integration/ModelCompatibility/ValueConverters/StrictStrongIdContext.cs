namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Uses strict sibling ordering for converted keys.</summary>
internal sealed class StrictStrongIdContext : OrderedStrongIdContext
{
    /// <summary>Creates the strict converted-key context.</summary>
    internal StrictStrongIdContext(
        DbContextOptions<StrictStrongIdContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override NestedSetOrderMode Mode => NestedSetOrderMode.Strict;

    /// <inheritdoc />
    protected override string Table => "StrictStrongIdNodes";
}

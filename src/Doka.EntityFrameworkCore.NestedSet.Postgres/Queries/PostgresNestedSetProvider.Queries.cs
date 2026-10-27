namespace Doka.EntityFrameworkCore.NestedSet.Postgres;

internal sealed partial class PostgresNestedSetProvider
{
    /// <inheritdoc />
    internal override bool UsesQueryAnchorKeyset => true;

    /// <inheritdoc />
    internal override bool SupportsTrackedKeyCollection(
        IProperty key
    )
    {
        if (key.GetTypeMapping()
                .Converter is not null)
        {
            return false;
        }

        var type = key.ClrType;

        // WHY: Broader CLR integral or converted types do not share the qualified native array contract.
        return type == typeof(byte)
            || type == typeof(short)
            || type == typeof(int)
            || type == typeof(long)
            || type == typeof(Guid);
    }
}

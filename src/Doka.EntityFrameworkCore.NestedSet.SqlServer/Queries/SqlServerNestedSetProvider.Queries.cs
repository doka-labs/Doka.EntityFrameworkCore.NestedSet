namespace Doka.EntityFrameworkCore.NestedSet.SqlServer;

internal sealed partial class SqlServerNestedSetProvider
{
    /// <inheritdoc />
    internal override bool UsesIndependentAncestorOwners => true;

    /// <inheritdoc />
    internal override NestedSetNearestQueryStrategy NearestQueryStrategy => NestedSetNearestQueryStrategy.ProjectedDepth;

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

        // WHY: Keep unsigned and converted keys outside the verified SQL Server collection transport.
        return type == typeof(byte)
            || type == typeof(short)
            || type == typeof(int)
            || type == typeof(long)
            || type == typeof(Guid);
    }
}

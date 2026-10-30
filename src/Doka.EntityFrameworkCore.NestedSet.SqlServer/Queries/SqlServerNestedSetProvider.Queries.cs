namespace Doka.EntityFrameworkCore.NestedSet.SqlServer;

internal sealed partial class SqlServerNestedSetProvider
{
    /// <inheritdoc />
    internal override bool UsesIndependentAncestorOwners => true;

    /// <inheritdoc />
    internal override bool TagsCompositionQueries => true;

    /// <inheritdoc />
    internal override bool CompletesFactSelections => true;

    /// <inheritdoc />
    internal override void AddServices(
        IServiceCollection services
    ) => services.TryAddEnumerable(ServiceDescriptor.Singleton<IInterceptor, SqlServerCompositionPlanInterceptor>());

    /// <inheritdoc />
    // WHY: Valid ancestors form one nested chain, so descending Left selects the same nearest
    // candidate as descending Depth and exposes the existing coordinate-index order without a depth sort.
    internal override NestedSetNearestQueryStrategy NearestQueryStrategy => NestedSetNearestQueryStrategy.ProjectedLeft;

    /// <inheritdoc />
    internal override NestedSetPathFailureStrategy PathFailureStrategy => NestedSetPathFailureStrategy.CompletedPoints;

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

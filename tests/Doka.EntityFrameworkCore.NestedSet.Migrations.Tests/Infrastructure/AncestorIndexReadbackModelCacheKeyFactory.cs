namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>Prevents declared-trigger metadata from leaking into the ordinary readback model.</summary>
public sealed class AncestorIndexReadbackModelCacheKeyFactory : IModelCacheKeyFactory
{
    /// <inheritdoc />
    public object Create(
        DbContext context,
        bool designTime
    )
    {
        var readback = (AncestorIndexReadbackContext)context;

        return (context.GetType(), readback.DeclaredTrigger, designTime);
    }
}

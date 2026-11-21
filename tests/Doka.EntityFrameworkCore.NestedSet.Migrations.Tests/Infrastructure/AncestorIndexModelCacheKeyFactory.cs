namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>Separates enabled, ordinary, and application-owned migration models.</summary>
public sealed class AncestorIndexModelCacheKeyFactory : IModelCacheKeyFactory
{
    /// <inheritdoc />
    public object Create(
        DbContext context,
        bool designTime
    )
    {
        var model = (AncestorIndexContext)context;

        return (context.GetType(), model.Enabled, model.Schema, model.AdoptIndex, model.SplitStructure, designTime);
    }
}

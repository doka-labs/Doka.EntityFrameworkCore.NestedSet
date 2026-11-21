namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>Separates historical models and schemas while retaining EF's ordinary model-building services.</summary>
public sealed class MigrationModelCacheKeyFactory : IModelCacheKeyFactory
{
    /// <inheritdoc />
    public object Create(
        DbContext context,
        bool designTime
    )
    {
        var migrationContext = (MigrationContext)context;

        return (context.GetType(), migrationContext.Stage, migrationContext.Schema, designTime);
    }
}

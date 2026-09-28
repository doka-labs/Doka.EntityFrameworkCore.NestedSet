namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Provides shared test operations for library-owned tree registries.</summary>
internal static class NestedSetTestInfrastructure
{
    /// <summary>Deletes every registry row in the supplied test model.</summary>
    /// <param name="context">The context owning the model and open test database.</param>
    /// <param name="cancellationToken">The token used for each database command.</param>
    /// <returns>A task that completes after all distinct registry tables are empty.</returns>
    internal static async Task ClearRegistriesAsync(
        DbContext context,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(context);

        var sql = context.GetService<ISqlGenerationHelper>();
        var registries = context
            .Model
            .GetEntityTypes()
            .Where(entity => entity.ClrType == typeof(NestedSetTreeRegistry))
            .Select(entity => (Name: entity.GetTableName()!, Schema: entity.GetSchema()))
            .Distinct();

        foreach (var (name, schema) in registries)
        {
            var table = sql.DelimitIdentifier(name, schema);
            var deleteRegistry = "DELETE FROM " + table;

            // WHY: Registry entity names depend on the mapped hierarchy, so EF metadata is the only stable source.
            await context.Database.ExecuteSqlRawAsync(deleteRegistry, cancellationToken);
        }
    }

    /// <summary>Determines whether a command targets any library-owned registry table in the current model.</summary>
    /// <param name="context">The command context, or <see langword="null" /> for provider-only commands.</param>
    /// <param name="commandText">The command text observed by an EF interceptor.</param>
    /// <returns><see langword="true" /> when a mapped registry table is referenced.</returns>
    internal static bool ReferencesRegistry(
        DbContext? context,
        string commandText
    )
    {
        if (context is null)
        {
            return false;
        }

        foreach (var entity in context.Model.GetEntityTypes())
        {
            if (entity.ClrType != typeof(NestedSetTreeRegistry))
            {
                continue;
            }

            var table = entity.GetTableName();
            if (table is not null
                && commandText.Contains(table, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

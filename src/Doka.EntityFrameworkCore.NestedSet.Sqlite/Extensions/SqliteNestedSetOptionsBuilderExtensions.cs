namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Registers nested-set services within the SQLite provider's native options callback.</summary>
public static class SqliteNestedSetOptionsBuilderExtensions
{
    /// <summary>Registers the common hierarchy engine and its matching Sqlite adapter.</summary>
    /// <param name="optionsBuilder">The native provider options builder being configured.</param>
    /// <returns>The same native provider builder for further configuration.</returns>
    /// <exception cref="ArgumentNullException">The native options builder is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Another adapter is selected or the EF insertion contract is unavailable.
    /// </exception>
    public static SqliteDbContextOptionsBuilder UseNestedSets(
        this SqliteDbContextOptionsBuilder optionsBuilder
    )
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        var core = ((IRelationalDbContextOptionsBuilderInfrastructure)optionsBuilder).OptionsBuilder;
        NestedSetProviderRegistration.Register(core, Sqlite.SqliteNestedSetProvider.Instance);

        return optionsBuilder;
    }
}

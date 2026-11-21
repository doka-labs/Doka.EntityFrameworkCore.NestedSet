using Doka.EntityFrameworkCore.NestedSet.Sqlite;

namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Registers nested-set services within the SQLite provider's native options callback.</summary>
[PublicAPI]
public static class SqliteNestedSetOptionsBuilderExtensions
{
    /// <summary>
    /// Enables the hierarchy engine through the selected native provider builder.
    /// </summary>
    /// <param name="optionsBuilder">The native provider options builder being configured.</param>
    extension(SqliteDbContextOptionsBuilder optionsBuilder)
    {
        /// <summary>Registers the common hierarchy engine and its matching Sqlite adapter.</summary>
        /// <returns>The same native provider builder for further configuration.</returns>
        /// <exception cref="ArgumentNullException">The native options builder is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Another adapter is selected or the EF insertion contract is unavailable.
        /// </exception>
        public SqliteDbContextOptionsBuilder UseNestedSets()
        {
            ArgumentNullException.ThrowIfNull(optionsBuilder);
            var core = ((IRelationalDbContextOptionsBuilderInfrastructure)optionsBuilder).OptionsBuilder;
            NestedSetProviderRegistration.Register(core, SqliteNestedSetProvider.Instance);

            return optionsBuilder;
        }
    }
}

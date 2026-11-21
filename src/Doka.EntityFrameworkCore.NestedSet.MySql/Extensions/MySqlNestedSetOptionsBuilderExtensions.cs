using Doka.EntityFrameworkCore.MySql;
using Doka.EntityFrameworkCore.NestedSet.MySql;

namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Registers nested-set services within the MySQL provider's native options callback.</summary>
[PublicAPI]
public static class MySqlNestedSetOptionsBuilderExtensions
{
    /// <summary>
    /// Enables the hierarchy engine through the selected native provider builder.
    /// </summary>
    /// <param name="optionsBuilder">The native provider options builder being configured.</param>
    extension(MySqlDbContextOptionsBuilder optionsBuilder)
    {
        /// <summary>Registers the common hierarchy engine and its matching MySql adapter.</summary>
        /// <returns>The same native provider builder for further configuration.</returns>
        /// <exception cref="ArgumentNullException">The native options builder is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Another adapter is selected or the EF insertion contract is unavailable.
        /// </exception>
        public MySqlDbContextOptionsBuilder UseNestedSets()
        {
            ArgumentNullException.ThrowIfNull(optionsBuilder);
            var core = ((IRelationalDbContextOptionsBuilderInfrastructure)optionsBuilder).OptionsBuilder;
            NestedSetProviderRegistration.Register(core, MySqlNestedSetProvider.Instance);

            return optionsBuilder;
        }
    }
}

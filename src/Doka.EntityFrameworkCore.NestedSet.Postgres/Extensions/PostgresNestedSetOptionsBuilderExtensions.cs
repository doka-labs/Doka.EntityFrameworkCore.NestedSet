using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Registers nested-set services within the PostgreSQL provider's native options callback.</summary>
public static class PostgresNestedSetOptionsBuilderExtensions
{
    /// <summary>Registers the common hierarchy engine and its matching Postgres adapter.</summary>
    /// <param name="optionsBuilder">The native provider options builder being configured.</param>
    /// <returns>The same native provider builder for further configuration.</returns>
    /// <exception cref="ArgumentNullException">The native options builder is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Another adapter is selected or the EF insertion contract is unavailable.
    /// </exception>
    public static NpgsqlDbContextOptionsBuilder UseNestedSets(
        this NpgsqlDbContextOptionsBuilder optionsBuilder
    )
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        var core = ((IRelationalDbContextOptionsBuilderInfrastructure)optionsBuilder).OptionsBuilder;
        NestedSetProviderRegistration.Register(core, Postgres.PostgresNestedSetProvider.Instance);

        return optionsBuilder;
    }
}

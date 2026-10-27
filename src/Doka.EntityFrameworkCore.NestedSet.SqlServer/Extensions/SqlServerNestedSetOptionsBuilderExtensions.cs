namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Registers nested-set services within the SQL Server provider's native options callback.</summary>
public static class SqlServerNestedSetOptionsBuilderExtensions
{
    /// <summary>Registers the common hierarchy engine and its matching SqlServer adapter.</summary>
    /// <param name="optionsBuilder">The native provider options builder being configured.</param>
    /// <returns>The same native provider builder for further configuration.</returns>
    /// <exception cref="ArgumentNullException">The native options builder is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Another adapter is selected or the EF insertion contract is unavailable.
    /// </exception>
    public static SqlServerDbContextOptionsBuilder UseNestedSets(
        this SqlServerDbContextOptionsBuilder optionsBuilder
    )
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        var core = ((IRelationalDbContextOptionsBuilderInfrastructure)optionsBuilder).OptionsBuilder;
        NestedSetProviderRegistration.Register(core, SqlServer.SqlServerNestedSetProvider.Instance);

        return optionsBuilder;
    }
}

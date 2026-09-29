namespace Doka.EntityFrameworkCore.NestedSet.Samples.Kpis;

/// <summary>Provides construct-only design-time configuration for the Doka MySQL/MariaDB migration set.</summary>
public sealed class KpiContextFactory : IDesignTimeDbContextFactory<KpiContext>
{
    /// <summary>Creates a Doka context with MariaDB by default, or MySQL when explicitly selected.</summary>
    /// <param name="args">Optional sample arguments, including <c>--provider mysql</c>.</param>
    /// <returns>A configured context without database I/O or schema creation.</returns>
    /// <exception cref="ArgumentException">The SQLite provider is selected for the Doka migration context.</exception>
    public KpiContext CreateDbContext(
        string[] args
    )
    {
        var command = SampleCommand.Parse(args, Program.ScenarioNames);
        if (command.Provider == SampleProvider.Sqlite)
        {
            throw new ArgumentException("Use KpiSqliteContextFactory for the SQLite migration set.", nameof(args));
        }

        var configuration = SampleDatabaseConfiguration.Create("kpis", command.Provider);

        return new KpiContext(configuration.CreateOptions<KpiContext>());
    }
}

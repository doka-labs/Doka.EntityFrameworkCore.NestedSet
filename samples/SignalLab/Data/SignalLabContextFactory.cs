namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Provides construct-only design-time configuration for the Doka MySQL/MariaDB migration set.</summary>
public sealed class SignalLabContextFactory : IDesignTimeDbContextFactory<SignalLabContext>
{
    /// <summary>Creates a Doka context with MariaDB by default, or MySQL when explicitly selected.</summary>
    /// <param name="args">Optional sample arguments, including <c>--provider mysql</c>.</param>
    /// <returns>A configured context without database I/O or schema creation.</returns>
    /// <exception cref="ArgumentException">The SQLite provider is selected for the Doka migration context.</exception>
    public SignalLabContext CreateDbContext(
        string[] args
    )
    {
        var command = SampleCommand.Parse(args, Program.ScenarioNames);
        if (command.Provider == SampleProvider.Sqlite)
        {
            throw new ArgumentException(
                "Use SignalLabSqliteContextFactory for the SQLite migration set.",
                nameof(args));
        }

        var configuration = SampleDatabaseConfiguration.Create("signal-lab", command.Provider);

        return new SignalLabContext(configuration.CreateOptions<SignalLabContext>());
    }
}

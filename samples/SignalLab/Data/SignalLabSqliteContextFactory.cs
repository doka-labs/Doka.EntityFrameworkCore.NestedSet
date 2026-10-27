namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Provides construct-only design-time configuration for the optional SQLite migration set.</summary>
public sealed class SignalLabSqliteContextFactory : IDesignTimeDbContextFactory<SignalLabSqliteContext>
{
    /// <summary>Creates the SQLite context without database I/O or schema creation.</summary>
    /// <param name="args">Arguments supplied by EF tooling; the context type always selects SQLite.</param>
    /// <returns>A context configured for the sample's dedicated SQLite database.</returns>
    public SignalLabSqliteContext CreateDbContext(
        string[] args
    )
    {
        var configuration = SampleDatabaseConfiguration.Create("signal-lab", SampleProvider.Sqlite);

        return new SignalLabSqliteContext(configuration.CreateOptions<SignalLabSqliteContext>());
    }
}

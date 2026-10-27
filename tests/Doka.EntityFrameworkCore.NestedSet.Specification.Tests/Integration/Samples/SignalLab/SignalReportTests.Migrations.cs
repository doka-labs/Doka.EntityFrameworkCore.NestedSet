using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;
using Microsoft.EntityFrameworkCore.Migrations;
using LabInstrument = Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab.Instrument;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SignalReportTests
{
    /// <summary>The executable provider lines apply the real initial and single additive reporting migration.</summary>
    [EngineFact(ExcludedEngines = ["PostgreSql", "SqlServer"],
        Reason = "The console sample publishes Doka and SQLite migration lines; "
            + "PostgreSQL and SQL Server qualify the common reporting model and queries.")]
    public async Task SampleMigrationChainCreatesTypedReportingSchema()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine, static _ => Task.CompletedTask);
        await using var source = database.CreateContext();
        await using var context = CreateMigrationContext(source.Database.GetConnectionString()!);

        // Act
        await context.Database.MigrateAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).Count());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync(CancellationToken.None));
        Assert.Equal(0, await context.TagValues.CountAsync(CancellationToken.None));
        Assert.Equal(0, await context.Instruments.CountAsync(CancellationToken.None));
    }

    /// <summary>
    /// The additive acquisition migration preserves existing instrument rows and maintained coordinates.
    /// </summary>
    [EngineFact(ExcludedEngines = ["PostgreSql", "SqlServer"],
        Reason = "The console sample publishes Doka and SQLite migration lines; "
            + "PostgreSQL and SQL Server have no executable sample migration set.")]
    public async Task ReportingMigrationPreservesExistingInstrumentData()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine, static _ => Task.CompletedTask);
        await using var source = database.CreateContext();
        await using var context = CreateMigrationContext(source.Database.GetConnectionString()!);
        var initial = context.Database.GetMigrations().First();
        await context.GetService<IMigrator>().MigrateAsync(initial, CancellationToken.None);
        var root = new LabInstrument { NodeId = Guid.NewGuid(), Title = "Existing instrument", PulseCount = 12 };
        var scope = Guid.NewGuid();
        var tree = Guid.NewGuid();
        await context.NestedSet<LabInstrument>().ForScope(scope).InsertRootAsync(root, tree, CancellationToken.None);
        context.ChangeTracker.Clear();

        // Act
        await context.Database.MigrateAsync(CancellationToken.None);

        // Assert
        var preserved = await context.Instruments.AsNoTracking().SingleAsync(value => value.NodeId == root.NodeId,
            CancellationToken.None);

        Assert.Equal("Existing instrument", preserved.Title);
        Assert.Equal(12, preserved.PulseCount);
        Assert.Equal((scope, tree, 1L, 2L), (preserved.ProjectId, preserved.TreeId, preserved.Start, preserved.End));
        Assert.Equal(2, (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).Count());
    }

    /// <summary>Selects the real executable context whose checked-in migration set belongs to this provider.</summary>
    private SignalLabContext CreateMigrationContext(string connection) => Engine == "Sqlite"
        ? new SignalLabSqliteContext(ModelCompatibilityDatabase.Options<SignalLabSqliteContext>(Engine, connection))
        : new SignalLabContext(ModelCompatibilityDatabase.Options<SignalLabContext>(Engine, connection));
}

namespace Doka.EntityFrameworkCore.NestedSet.SafeMigrations.Tests;

public sealed partial class SafeMigrationTests
{
    /// <summary>Verifies foreign database qualifiers cannot change either populated hierarchy database.</summary>
    /// <param name="engine">The MySQL-compatible relational database engine.</param>
    /// <returns>A task that completes after analysis and runtime execution reject the mismatched database.</returns>
    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    public async Task ForeignDatabaseQualifierIsRejectedBeforeEitherDatabaseChanges(
        string engine
    )
    {
        // Arrange
        await using var selected = await CreateDatabaseAsync(engine, true);
        await using var foreign = await CreateDatabaseAsync(engine, true);
        var selectedBaseline = selected.Scaffold(MigrationStage.Baseline, "SafeSelectedBaseline");
        var foreignBaseline = foreign.Scaffold(MigrationStage.Baseline, "SafeForeignBaseline");
        await selected.MigrateAsync(selectedBaseline);
        await foreign.MigrateAsync(foreignBaseline);
        await selected.SeedAsync();
        await foreign.SeedAsync();

        // WHY: Analysis requires the runtime model and migration snapshot to agree before checking qualifiers.
        var selectedUpgrade = selected.Scaffold(MigrationStage.Current, "SafeSelectedScopeKey", selectedBaseline);
        var foreignUpgrade = foreign.Scaffold(MigrationStage.Current, "SafeForeignScopeKey", foreignBaseline);
        await using var foreignContext = foreign.CreateContext(MigrationStage.Current, foreignUpgrade.Assembly);
        await using var selectedContext = selected.CreateContext(MigrationStage.Current, selectedUpgrade.Assembly);
        var operation = GetIndexOperations(foreignUpgrade, foreignContext, 1)
            .Single(candidate => ((EnsureIndexIntent)candidate.Intent)
                .Definition
                .Keys
                .Select(key => key.Column)
                .SequenceEqual(["tree_scope", "tree_id", "left_bound"]));

        var definition = ((EnsureIndexIntent)operation.Intent).Definition;
        var selectedIndexesBefore = IndexSignatures(await selected.ReadIndexesAsync());
        var foreignIndexesBefore = IndexSignatures(await foreign.ReadIndexesAsync());
        var selectedNodesBefore = await selected.ReadNodesAsync();
        var foreignNodesBefore = await foreign.ReadNodesAsync();

        // WHY: Both databases have the same table and data. Stripping or honoring the foreign qualifier would
        // create the missing index in one database, so both physical catalogs must remain unchanged.
        // Act
        var report = await selectedContext
            .GetService<ISafeMigrationRunner>()
            .AnalyzeAsync(
                selectedContext,
                [operation],
                new SafeMigrationRunOptions("nested-set-foreign-database-qualifier"),
                CancellationToken.None);

        var exception = await Record.ExceptionAsync(() =>
            SafeMigrationTestServices.ExecuteOperationsAsync(selectedContext, [operation]));

        // Assert
        Assert.NotNull(foreign.Schema);
        Assert.NotEqual(selected.Schema, foreign.Schema);
        Assert.Equal(foreign.Schema, definition.Schema);
        Assert.Equal(SafeMigrationReportStatus.Blocked, report.Status);
        var assessment = Assert.Single(report.Assessments);
        Assert.Equal(SafeMigrationObservedState.Unsupported, assessment.ObservedState);
        Assert.Equal(SafeMigrationAction.RejectUnsupported, assessment.Action);
        Assert.Equal("database_qualifier_mismatch", assessment.AnalysisCode);
        var databaseException = Assert.IsAssignableFrom<DbException>(exception);
        Assert.Contains("doka_sm_unsupported", databaseException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(selectedIndexesBefore, IndexSignatures(await selected.ReadIndexesAsync()));
        Assert.Equal(foreignIndexesBefore, IndexSignatures(await foreign.ReadIndexesAsync()));
        Assert.Equal(selectedNodesBefore, await selected.ReadNodesAsync());
        Assert.Equal(foreignNodesBefore, await foreign.ReadNodesAsync());
    }
}

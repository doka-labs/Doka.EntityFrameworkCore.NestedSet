namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies owned fixture resources and error identity when initialization cannot return a database.</summary>
public abstract class DatabaseLifecycleTests : ProviderTest
{
    /// <summary>Uses the concrete provider fixture as the immutable engine owner.</summary>
    /// <param name="fixture">The fixture bound to this provider suite.</param>
    protected DatabaseLifecycleTests(
        IProviderFixture<ProviderResources> fixture
    ) : base(fixture) { }

    /// <summary>Resolves the executable assembly's server owner from a method in the shared library.</summary>
    [DatabaseIndependent]
    [Fact]
    public async Task SharedLibraryResolvesExecutingAssemblyFixture()
    {
        // Arrange
        var expected = await TestContext.Current.GetFixture<TestDatabaseServers>();

        // Act
        var actual = await TestDatabaseServers.GetCurrentAsync();

        // Assert
        Assert.NotNull(expected);
        Assert.Same(expected, actual);
        Assert.NotEqual(typeof(TestDatabaseServers).Assembly, GetType().Assembly);
        Assert.True(ProviderEngineOwnership.Includes(Engine));
    }

    /// <summary>Rejects a foreign engine before the shared fixture can start its container.</summary>
    [DatabaseIndependent]
    [Fact]
    public async Task AssemblyFixtureRejectsForeignServer()
    {
        // Arrange
        var foreignEngine = Engine is "MySql" or "MariaDb" ? "PostgreSql" : "MySql";

        var fixture = await TestDatabaseServers.GetCurrentAsync();

        // Act
        var error = await Record.ExceptionAsync(() => fixture.NewConnectionStringAsync(foreignEngine));

        // Assert
        Assert.True(ProviderEngineOwnership.Includes(Engine));
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains(foreignEngine, error.Message, StringComparison.Ordinal);
    }

    /// <summary>Concurrent fixtures share the engine but never observe each other's rows.</summary>
    [EngineFact(
        ExcludedEngines = ["Sqlite"],
        Reason = "This lifecycle assertion compares two server database names sharing one server; "
            + "SQLite uses isolated files.")]
    public async Task ServerDatabasesShareEngineWithoutSharingRows()
    {
        // Arrange
        await using var first = await TestDatabase.CreateAsync(Engine);
        await using var second = await TestDatabase.CreateAsync(Engine);
        await using var firstContext = first.CreateContext();
        await using var secondContext = second.CreateContext();
        await firstContext
            .Set<UnrelatedRow>()
            .AddAsync(
                new UnrelatedRow
                {
                    Id = 1,
                    Value = "first",
                },
                CancellationToken.None);

        // Act
        await firstContext.SaveChangesAsync(CancellationToken.None);
        var secondCount = await secondContext
            .Set<UnrelatedRow>()
            .CountAsync(CancellationToken.None);

        // Assert
        Assert.Equal(
            firstContext.Database.GetDbConnection().DataSource,
            secondContext.Database.GetDbConnection().DataSource);
        Assert.NotEqual(
            firstContext.Database.GetDbConnection().Database,
            secondContext.Database.GetDbConnection().Database);
        Assert.Equal(0, secondCount);
    }
}

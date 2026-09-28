namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Owns a database per engine while isolating the state of each relational test case.</summary>
/// <remarks>xUnit serializes cases in the consuming class, so table resets cannot overlap another case.</remarks>
public sealed class RelationalFixture : IAsyncLifetime
{
    private readonly Dictionary<string, TestDatabase> _databases = new(StringComparer.Ordinal);

    /// <summary>
    ///     Initializes the fixture; engines start lazily so filtered tests create only required containers.
    /// </summary>
    /// <returns>An already completed task.</returns>
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>Returns an engine with empty entity tables and no nested-set lock rows.</summary>
    /// <param name="engine">The supported engine name.</param>
    /// <returns>The fixture-owned database, whose lifetime must not be disposed by individual cases.</returns>
    public async Task<TestDatabase> ResetAsync(
        string engine
    )
    {
        if (!_databases.TryGetValue(engine, out var database))
        {
            database = await TestDatabase.CreateAsync(engine);
            _databases.Add(engine, database);
        }

        // WHY: Reuse this class's schema while resetting rows; other classes have separate databases on the server.
        await database.ResetAsync();

        return database;
    }

    /// <summary>Disposes the isolated databases and SQLite files created by this fixture.</summary>
    /// <returns>A task that completes after all fixture-owned resources have been disposed.</returns>
    public async ValueTask DisposeAsync()
    {
        foreach (var database in _databases.Values)
        {
            await database.DisposeAsync();
        }
    }
}

using Doka.EntityFrameworkCore.NestedSet.Providers;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>
/// Preserves selected adapter ownership through real provider callback and context-service graphs.
/// </summary>
public sealed class SelectedAdapterResolutionTests
{
    /// <summary>
    /// Adapter ownership remains exact, including after a full GC.
    /// </summary>
    /// <param name="engine">The actual native adapter configured in the metadata-only context.</param>
    /// <param name="collectBeforeResolution">
    /// Whether weak runtime caches are challenged after warming the lookup.
    /// </param>
    [Theory]
    [InlineData("MySql", false)]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", false)]
    [InlineData("MariaDb", true)]
    [InlineData("PostgreSql", false)]
    [InlineData("PostgreSql", true)]
    [InlineData("Sqlite", false)]
    [InlineData("Sqlite", true)]
    [InlineData("SqlServer", false)]
    [InlineData("SqlServer", true)]
    public void Resolve_SelectedAdapter_RetainsOwnershipAcrossCollection(
        string engine,
        bool collectBeforeResolution
    )
    {
        // Arrange
        var options = ModelCompatibilityDatabase.Options<DbContext>(engine, Connection(engine));
        using var services = OwnedModelTestServices<DbContext>.Create(options);
        using var context = new DbContext(services.Options);
        var expected = NestedSetProviderCapabilities.Resolve(context);

        if (collectBeforeResolution)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        }

        // Act
        var actual = NestedSetProviderCapabilities.Resolve(context);

        // Assert
        Assert.Same(expected, actual);
        Assert.Same(NestedSetTestAdapters.ForEngine(engine), expected);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);

        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>Supplies valid inert connection text so initialization cannot require an available database.</summary>
    private static string Connection(
        string engine
    ) => engine switch
    {
        "MySql" or "MariaDb" => "Server=127.0.0.1;Database=adapter_allocation;User ID=unused;Pooling=false",
        "PostgreSql" => "Host=127.0.0.1;Database=adapter_allocation;Username=unused;Pooling=false",
        "SqlServer" =>
            "Server=127.0.0.1;Database=adapter_allocation;User ID=unused;Password=unused;"
            + "TrustServerCertificate=true;Pooling=false",
        "Sqlite" => "Data Source=:memory:",
        _ => throw new ArgumentOutOfRangeException(nameof(engine)),
    };
}

using System.Runtime.CompilerServices;
using Doka.EntityFrameworkCore.NestedSet.Providers;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Preserves allocation-free warm resolution through real provider callback and context-service graphs.</summary>
[Collection("Allocation measurements")]
public sealed class SelectedAdapterAllocationTests
{
    /// <summary>Warm adapter ownership checks do not recreate metadata or wrappers, including after a full GC.</summary>
    /// <param name="engine">The actual native adapter configured in the metadata-only context.</param>
    /// <param name="collectBeforeMeasurement">Whether weak runtime caches are challenged after warming the lookup.</param>
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
    public void WarmSelectedAdapterResolutionDoesNotAllocate(string engine, bool collectBeforeMeasurement)
    {
        // Arrange
        var options = ModelCompatibilityDatabase.Options<DbContext>(engine, Connection(engine));
        using var services = OwnedModelTestServices<DbContext>.Create(options);
        using var context = new DbContext(services.Options);
        var expected = NestedSetProviderCapabilities.Resolve(context);

        // WHY: Complete the real service initialization and the exact measurement method before
        // measuring ownership lookup; query/model construction and first use are separate operations.
        for (var repetition = 0; repetition < 8; repetition++)
        {
            MeasureLookups(context);
        }

        if (collectBeforeMeasurement)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        }

        // Act
        var allocated = MeasureLookups(context);

        // Assert
        Assert.Equal(0, allocated);
        Assert.Same(NestedSetTestAdapters.ForEngine(engine), expected);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>Measures only the real already initialized context-to-adapter ownership path.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long MeasureLookups(DbContext context)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < 10_000; index++)
        {
            GC.KeepAlive(NestedSetProviderCapabilities.Resolve(context));
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    /// <summary>Supplies valid inert connection text so initialization cannot require an available database.</summary>
    private static string Connection(string engine) => engine switch
    {
        "MySql" or "MariaDb" => "Server=127.0.0.1;Database=adapter_allocation;User ID=unused;Pooling=false",
        "PostgreSql" => "Host=127.0.0.1;Database=adapter_allocation;Username=unused;Pooling=false",
        "SqlServer" => "Server=127.0.0.1;Database=adapter_allocation;User ID=unused;Password=unused;TrustServerCertificate=true;Pooling=false",
        "Sqlite" => "Data Source=:memory:",
        _ => throw new ArgumentOutOfRangeException(nameof(engine)),
    };
}

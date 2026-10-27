using System.Runtime.CompilerServices;
using Doka.EntityFrameworkCore.NestedSet.Providers;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class DokaStraightJoinTests
{
    /// <summary>The actual typed ordered-join marker identity needs no per-query lookup allocations.</summary>
    [Trait("Category", "Allocation")]
    [Fact]
    public void WarmTypedMethodIdentityLookupDoesNotAllocate()
    {
        // Arrange
        var adapter = NestedSetTestAdapters.ForEngine("MySql");
        var method = adapter.OrderedJoinMethod;

        for (var repetition = 0; repetition < 8; repetition++)
        {
            MeasureMethodLookups(adapter);
        }

        // Act
        var allocated = MeasureMethodLookups(adapter);

        // Assert
        Assert.NotNull(method);
        Assert.Same(method, adapter.OrderedJoinMethod);
        Assert.Equal(0, allocated);
    }

    /// <summary>Measures only reads of the already initialized public operator identity.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long MeasureMethodLookups(NestedSetProviderCapabilities adapter)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < 10_000; index++)
        {
            GC.KeepAlive(adapter.OrderedJoinMethod);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}

using System.Runtime.CompilerServices;
using Doka.EntityFrameworkCore.NestedSet.Providers;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class DokaSourceBindingTests
{
    /// <summary>Reading the actual typed marker identity allocates no replacement reflection metadata.</summary>
    [Trait("Category", "Allocation")]
    [Fact]
    public void WarmTypedMethodIdentityLookupDoesNotAllocate()
    {
        // Arrange
        var adapter = NestedSetTestAdapters.ForEngine("MySql");
        var method = adapter.CompletedSourceMethod;

        for (var repetition = 0; repetition < 8; repetition++)
        {
            MeasureMethodLookups(adapter);
        }

        // Act
        var allocated = MeasureMethodLookups(adapter);

        // Assert
        Assert.NotNull(method);
        Assert.Same(method, adapter.CompletedSourceMethod);
        Assert.Equal(0, allocated);
    }

    /// <summary>Measures only repeated reads of the adapter's already initialized exact public marker.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long MeasureMethodLookups(NestedSetProviderCapabilities adapter)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < 10_000; index++)
        {
            GC.KeepAlive(adapter.CompletedSourceMethod);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}

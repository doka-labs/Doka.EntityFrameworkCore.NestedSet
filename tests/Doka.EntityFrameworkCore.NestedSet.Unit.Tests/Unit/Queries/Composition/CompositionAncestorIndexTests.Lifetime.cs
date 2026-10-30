using System.Runtime.CompilerServices;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class CompositionAncestorIndexTests
{
    /// <summary>The real EF compiled cache retains no originating context or IQueryable through a deferred marker.</summary>
    /// <param name="retainContext">Whether a deliberate strong context witness proves the lifetime check.</param>
    /// <param name="operation">The deferred ancestor operation or a chained filter.</param>
    [Theory]
    [InlineData(false, "ancestors")]
    [InlineData(true, "ancestors")]
    [InlineData(false, "path")]
    [InlineData(true, "path")]
    [InlineData(false, "any")]
    [InlineData(true, "any")]
    [InlineData(false, "nearest")]
    [InlineData(true, "nearest")]
    public async Task DeferredCompilationCacheDoesNotRetainContextOrQueryable(bool retainContext, string operation)
    {
        // Arrange
        var options = ModelCompatibilityDatabase.Options<IndexCompositionContext>("MariaDb",
            "Server=127.0.0.1;Database=deferred_lifetime;User ID=unused;Pooling=false");

        await using var provider = SelectionServices(options);
        var ownedOptions = new DbContextOptionsBuilder<IndexCompositionContext>(options)
            .UseInternalServiceProvider(provider).Options;

        var witnesses = PopulateDeferredCompilationCache(ownedOptions, retainContext, operation, out var retained);

        // Act
        CollectDeferredCompilationWitnesses();

        // Assert
        Assert.Equal(retainContext, witnesses[0].IsAlive);
        Assert.False(witnesses[1].IsAlive);
        // WHY: The actual compiled-query cache remains alive throughout the observation. Disposing
        // or clearing it before GC would hide a delegate retaining its original context or query.
        GC.KeepAlive(provider);
        GC.KeepAlive(retained);
    }

    /// <summary>Compiles and disposes in a separate JIT frame so setup locals cannot become accidental strong roots.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] PopulateDeferredCompilationCache(
        DbContextOptions<IndexCompositionContext> options,
        bool retainContext,
        string operation,
        out DbContext? retained
    )
    {
        // WHY: This metadata-only context performs no database I/O. Synchronous disposal keeps the
        // lifetime witness outside an async state machine that could retain its completed setup locals.
        // ReSharper disable once MethodHasAsyncOverload
        using var context = new IndexCompositionContext(options);
        var facts = context.Set<SelectionFact>();
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(Guid.Empty).InTree(Guid.Empty);
        var pairs = hierarchy
            .SelectAncestorsOrSelf(node => facts.Any(fact => fact.NodeId == node.Id),
                (node, owner, distance) => new { Node = node, Owner = owner, Distance = distance });

        var filtered = operation switch
        {
            "ancestors" => pairs,
            "path" => hierarchy.WherePathEdges(pairs, row => row.Owner, row => row.Node,
                (row, parent, child) => child.Id > 0),
            "any" => hierarchy.WhereAnyAncestorOrSelf(pairs, row => row.Node, (row, owner) => owner.Id > 0),
            "nearest" => hierarchy.WhereNearestAncestorOrSelf(pairs, row => row.Node,
                (row, owner) => owner.Id > 0, (row, owner) => owner.Visible),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        var query = filtered.Select(row => row.Owner.Id);

        Assert.Contains("EXISTS", query.ToQueryString(), StringComparison.OrdinalIgnoreCase);
        retained = retainContext ? context : null;

        return [new WeakReference(context), new WeakReference(query)];
    }

    /// <summary>Observes only complete collections and never reads a witness target inside the collecting frame.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CollectDeferredCompilationWitnesses()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
    }
}

using Doka.EntityFrameworkCore.NestedSet.Providers;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class CompositionAncestorIndexTests
{
    /// <summary>The typed adapter remains silent while a second context reuses a deferred query compilation.</summary>
    /// <param name="includeSelf">Whether the indexed relation additionally needs the ordered-join capability.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TypedAdapterDoesNotReportMissingFeaturesOnAnotherContextCacheHit(bool includeSelf)
    {
        // Arrange
        var events = new List<EventData>();
        var boundary = new CompositionIoBoundary();
        var probe = new DeferredCompilationProbe();
        var options = DeferredDiagnosticOptions(events, boundary);
        await using var provider = SelectionServices(options, probe);
        var isolated = new DbContextOptionsBuilder<IndexCompositionContext>(options)
            .UseInternalServiceProvider(provider).Options;

        await using var first = new IndexCompositionContext(isolated);
        await using var second = new IndexCompositionContext(isolated);
        var adapter = NestedSetProviderCapabilities.Resolve(first);
        var firstQuery = DeferredDiagnosticQuery(first, includeSelf);
        var secondQuery = DeferredDiagnosticQuery(second, includeSelf);
        var afterConstruction = events.Count;

        // Act
        var firstSql = firstQuery.ToQueryString();
        var afterFirst = events.Count;
        var secondSql = secondQuery.ToQueryString();

        // Assert
        Assert.Equal(0, afterConstruction);
        Assert.Equal(1, probe.Compilations);
        Assert.Equal(0, afterFirst);
        Assert.Equal(afterFirst, events.Count);
        Assert.Equal(firstSql, secondSql);
        Assert.Empty(events);
        Assert.NotNull(adapter.CompletedSourceMethod);
        Assert.NotNull(adapter.OrderedJoinMethod);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>An independent compilation remains silent when the selected typed adapter provides its required APIs.</summary>
    /// <param name="includeSelf">Whether the indexed relation additionally needs the ordered-join capability.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TypedAdapterDoesNotReportMissingFeaturesOnAnotherContextCacheMiss(bool includeSelf)
    {
        // Arrange
        var events = new List<EventData>();
        var boundary = new CompositionIoBoundary();
        var probe = new DeferredCompilationProbe();
        var options = DeferredDiagnosticOptions(events, boundary);
        await using var provider = SelectionServices(options, probe);
        var isolated = new DbContextOptionsBuilder<IndexCompositionContext>(options)
            .UseInternalServiceProvider(provider).Options;

        await using var first = new IndexCompositionContext(isolated);
        await using var second = new IndexCompositionContext(isolated);
        var adapter = NestedSetProviderCapabilities.Resolve(first);
        var firstQuery = DeferredDiagnosticQuery(first, includeSelf);
        var secondQuery = DeferredDiagnosticQuery(second, includeSelf).Select(id => new { Id = id });
        var afterConstruction = events.Count;

        // Act
        _ = firstQuery.ToQueryString();
        var afterFirst = events.Count;
        _ = secondQuery.ToQueryString();

        // Assert
        Assert.Equal(0, afterConstruction);
        Assert.Equal(2, probe.Compilations);
        Assert.Equal(0, afterFirst);
        Assert.Equal(afterFirst * 2, events.Count);
        Assert.Empty(events);
        Assert.NotNull(adapter.CompletedSourceMethod);
        Assert.NotNull(adapter.OrderedJoinMethod);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Owns logging and I/O observers in the same actual Doka service graph as the shared query cache.</summary>
    private static DbContextOptions<IndexCompositionContext> DeferredDiagnosticOptions(
        List<EventData> events,
        CompositionIoBoundary boundary
    ) => new DbContextOptionsBuilder<IndexCompositionContext>(
            ModelCompatibilityDatabase.Options<IndexCompositionContext>("MariaDb",
                "Server=127.0.0.1;Database=diagnostic_cache;User ID=unused;Pooling=false"))
        .AddInterceptors(boundary, boundary.Commands)
        .LogTo((eventId, _) => eventId.Id is >= 70_001 and <= 70_004, events.Add)
        .Options;

    /// <summary>Requests an ordinary captured fact selection whose capability decision belongs to compilation.</summary>
    private static IQueryable<int> DeferredDiagnosticQuery(IndexCompositionContext context, bool includeSelf)
    {
        var facts = context.Set<SelectionFact>().Where(fact => fact.Id > 0);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(Guid.Empty).InTree(Guid.Empty);

        return includeSelf
            ? hierarchy.SelectAncestorsOrSelf(node => facts.Any(fact => fact.NodeId == node.Id),
                (node, owner, distance) => owner.Id)
            : hierarchy.SelectAncestors(node => facts.Any(fact => fact.NodeId == node.Id),
                (node, owner, distance) => owner.Id);
    }

}

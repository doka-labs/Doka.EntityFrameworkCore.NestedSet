using System.Runtime.CompilerServices;
using Doka.EntityFrameworkCore.NestedSet.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class DokaQueryFeaturesTests
{
    private const string QueryEventPrefix = "Doka.EntityFrameworkCore.NestedSet.Query.";
    private const string AncestorFallbackName = QueryEventPrefix + "AncestorIndexWithoutReader";
    private const string AncestorFallbackMessage =
        "HasAncestorIndex is configured, but this provider has no optimized NestedSet ancestor reader. "
        + "The schema remains configured and this read uses the portable query strategy.";

    /// <summary>The schema-only fallback remains informational and occurs once for each actual context.</summary>
    [Fact]
    public void AncestorFallbackIsReportedOncePerContext()
    {
        // Arrange
        var events = new List<EventData>();
        var boundary = new CompositionIoBoundary();
        var options = DiagnosticOptions(events, boundary);
        using var provider = NestedSetTestAdapters.BuildServices(options);
        var isolated = IsolateDiagnosticOptions(options, provider);
        using var first = new CompositionGuardContext(isolated);
        using var second = new CompositionGuardContext(isolated);

        // Act
        NestedSetQueryDiagnostics.AncestorIndexWithoutReader(first);
        NestedSetQueryDiagnostics.AncestorIndexWithoutReader(first);
        NestedSetQueryDiagnostics.AncestorIndexWithoutReader(second);
        NestedSetQueryDiagnostics.AncestorIndexWithoutReader(second);

        // Assert
        Assert.Equal(2, events.Count);
        Assert.All(events, entry => Assert.Equal(LogLevel.Information, entry.LogLevel));
        Assert.All(events, entry => Assert.Equal(70_004, entry.EventId.Id));
        Assert.All(events, entry => Assert.Equal(AncestorFallbackName, entry.EventId.Name));
        Assert.All(events, entry => Assert.Equal(AncestorFallbackMessage, entry.ToString()));
        Assert.DoesNotContain(events, entry => entry.ToString().Contains("CompositionFirst", StringComparison.Ordinal));
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>A null context cannot consume another actual context's once-marker.</summary>
    [Fact]
    public void NullAncestorFallbackContextIsRejected()
    {
        // Arrange
        var events = new List<EventData>();
        var options = DiagnosticOptions(events);
        using var provider = NestedSetTestAdapters.BuildServices(options);
        using var context = new CompositionGuardContext(IsolateDiagnosticOptions(options, provider));

        // Act
        var error = Record.Exception(() => NestedSetQueryDiagnostics.AncestorIndexWithoutReader(null!));
        NestedSetQueryDiagnostics.AncestorIndexWithoutReader(context);

        // Assert
        Assert.Equal("context", Assert.IsType<ArgumentNullException>(error).ParamName);
        Assert.Equal(AncestorFallbackName, Assert.Single(events).EventId.Name);
    }

    /// <summary>Concurrent first reporting claims one bounded event after EF service initialization.</summary>
    [Fact]
    public async Task ConcurrentAncestorFallbackReportsOneEvent()
    {
        // Arrange
        var events = new List<EventData>();
        var options = DiagnosticOptions(events);
        using var provider = NestedSetTestAdapters.BuildServices(options);
        using var context = new CompositionGuardContext(IsolateDiagnosticOptions(options, provider));

        // WHY: EF contexts do not support concurrent initialization. Only the initialized once-marker is shared.
        _ = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();

        // Act
        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(
            () => NestedSetQueryDiagnostics.AncestorIndexWithoutReader(context), CancellationToken.None)));

        // Assert
        Assert.Single(events);
    }

    /// <summary>EF's explicit event filter suppresses the schema-only informational diagnosis.</summary>
    [Fact]
    public void ExplicitWarningFilterCanSuppressAncestorFallback()
    {
        // Arrange
        var events = new List<EventData>();
        var options = new DbContextOptionsBuilder<CompositionGuardContext>(DiagnosticOptions(events))
            .ConfigureWarnings(warnings => warnings.Ignore(new EventId(70_004, AncestorFallbackName)))
            .Options;

        using var provider = NestedSetTestAdapters.BuildServices(options);
        using var context = new CompositionGuardContext(IsolateDiagnosticOptions(options, provider));

        // Act
        NestedSetQueryDiagnostics.AncestorIndexWithoutReader(context);

        // Assert
        Assert.Empty(events);
    }

    /// <summary>DiagnosticSource filtering remains independent of EF's ILogger and simple-logging policy.</summary>
    /// <param name="enabled">Whether the actual DiagnosticSource subscription accepts the event.</param>
    /// <param name="ignored">Whether EF warning configuration suppresses ordinary logging.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AncestorFallbackHonorsIndependentDiagnosticSourceFiltering(bool enabled, bool ignored)
    {
        // Arrange
        var events = new List<EventData>();
        var builder = new DbContextOptionsBuilder<CompositionGuardContext>(DiagnosticOptions(events));

        if (ignored)
        {
            builder.ConfigureWarnings(warnings => warnings.Ignore(new EventId(70_004, AncestorFallbackName)));
        }

        var options = builder.Options;
        using var provider = NestedSetTestAdapters.BuildServices(options);
        using var context = new CompositionGuardContext(IsolateDiagnosticOptions(options, provider));
        var listener = Assert.IsType<DiagnosticListener>(context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>()
            .DiagnosticSource);

        using var observer = new QueryDiagnosticObserver();
        using var subscription = listener.Subscribe(observer, name => enabled && name == AncestorFallbackName);

        // Act
        NestedSetQueryDiagnostics.AncestorIndexWithoutReader(context);
        NestedSetQueryDiagnostics.AncestorIndexWithoutReader(context);

        // Assert
        if (enabled)
        {
            var dispatched = Assert.Single(observer.Events);
            var data = Assert.IsType<EventData>(dispatched.Value);

            Assert.Equal(AncestorFallbackName, dispatched.Key);
            Assert.Equal(AncestorFallbackMessage, data.ToString());

            if (!ignored)
            {
                Assert.Same(Assert.Single(events), data);
            }
        }
        else
        {
            Assert.Empty(observer.Events);
        }

        // WHY: EF10 checks DiagnosticSource.IsEnabled independently; ConfigureWarnings.Ignore applies
        // to ILogger and LogTo, so ignoring ordinary logs must not disable an explicitly subscribed observer.
        if (ignored)
        {
            Assert.Empty(events);
        }
        else
        {
            Assert.Equal(AncestorFallbackMessage, Assert.Single(events).ToString());
        }
    }

    /// <summary>Already reported fallback checks allocate no per-query objects.</summary>
    [Trait("Category", "Allocation")]
    [Fact]
    public void RepeatedAncestorFallbackChecksDoNotAllocate()
    {
        // Arrange
        var options = DiagnosticOptions(new List<EventData>());
        using var provider = NestedSetTestAdapters.BuildServices(options);
        using var context = new CompositionGuardContext(IsolateDiagnosticOptions(options, provider));
        NestedSetQueryDiagnostics.AncestorIndexWithoutReader(context);

        for (var repetition = 0; repetition < 8; repetition++)
        {
            MeasureFallbackChecks(context);
        }

        // Act
        var allocated = MeasureFallbackChecks(context);

        // Assert
        Assert.Equal(0, allocated);
    }

    /// <summary>The weak once-marker cannot extend the lifetime of its disposed context.</summary>
    [Fact]
    public void AncestorFallbackDoesNotRetainDisposedContext()
    {
        // Arrange
        var released = ReportAndReleaseContext();

        // Act
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        // Assert
        Assert.False(released.IsAlive);
    }

    /// <summary>Creates metadata-only SQLite options observed through EF's ordinary LogTo dispatch.</summary>
    private static DbContextOptions<CompositionGuardContext> DiagnosticOptions(
        List<EventData> events,
        CompositionIoBoundary? boundary = null
    )
    {
        var builder = new DbContextOptionsBuilder<CompositionGuardContext>()
            .UseSqlite("Data Source=:memory:", sqlite => sqlite.UseNestedSets()).LogTo(IsQueryEvent, events.Add);

        if (boundary is not null)
        {
            builder.AddInterceptors(boundary, boundary.Commands);
        }

        return builder.Options;
    }

    /// <summary>Filters only this library's bounded query events.</summary>
    private static bool IsQueryEvent(EventId eventId, LogLevel level)
        => eventId.Name?.StartsWith(QueryEventPrefix, StringComparison.Ordinal) is true;

    /// <summary>Keeps fresh diagnostic settings out of EF's process-global provider cache.</summary>
    private static DbContextOptions<CompositionGuardContext> IsolateDiagnosticOptions(
        DbContextOptions<CompositionGuardContext> options,
        ServiceProvider provider
    ) => new DbContextOptionsBuilder<CompositionGuardContext>(options).UseInternalServiceProvider(provider).Options;

    /// <summary>Measures only the already reported context's classification lookup.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long MeasureFallbackChecks(DbContext context)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < 10_000; index++)
        {
            NestedSetQueryDiagnostics.AncestorIndexWithoutReader(context);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    /// <summary>Ends the context's strong JIT lifetime before observing weak state collection.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ReportAndReleaseContext()
    {
        var options = DiagnosticOptions(new List<EventData>());
        using var provider = NestedSetTestAdapters.BuildServices(options);
        using var context = new CompositionGuardContext(IsolateDiagnosticOptions(options, provider));
        NestedSetQueryDiagnostics.AncestorIndexWithoutReader(context);

        return new WeakReference(context);
    }

    /// <summary>Limits process-wide diagnostic listener observations to the current test flow.</summary>
    private sealed class QueryDiagnosticObserver : IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private static readonly AsyncLocal<object?> s_owner = new();
        private readonly object _owner = new();
        private readonly object? _previous;

        /// <summary>Claims the current flow while retaining any outer observation scope.</summary>
        internal QueryDiagnosticObserver()
        {
            _previous = s_owner.Value;
            s_owner.Value = _owner;
        }

        /// <summary>Gets actual events emitted by this test flow.</summary>
        internal List<KeyValuePair<string, object?>> Events { get; } = [];

        /// <inheritdoc />
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (ReferenceEquals(s_owner.Value, _owner))
            {
                Events.Add(value);
            }
        }

        /// <inheritdoc />
        public void OnError(Exception error) => throw error;

        /// <inheritdoc />
        public void OnCompleted() { }

        /// <inheritdoc />
        public void Dispose() => s_owner.Value = _previous;
    }
}

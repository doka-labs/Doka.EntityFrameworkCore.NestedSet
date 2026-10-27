using Doka.EntityFrameworkCore.NestedSet.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class DokaQueryFeaturesTests
{
    /// <summary>The surviving schema-only fallback retains its exact ILogger template and EF LogTo message.</summary>
    [Fact]
    public void AncestorFallbackUsesEfLoggerAndSimpleLogging()
    {
        // Arrange
        var events = new List<EventData>();
        using var sink = new FallbackLoggerProvider();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(sink));
        var options = DiagnosticOptions(events);
        using var provider = NestedSetTestAdapters.BuildServices(options, services => services.AddSingleton(factory));
        using var context = new CompositionGuardContext(IsolateDiagnosticOptions(options, provider));

        // Act
        NestedSetQueryDiagnostics.AncestorIndexWithoutReader(context);
        NestedSetQueryDiagnostics.AncestorIndexWithoutReader(context);

        // Assert
        var entry = Assert.Single(sink.Events);
        var simple = Assert.Single(events);

        Assert.Equal(DbLoggerCategory.Query.Name, entry.Category);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(70_004, entry.Event.Id);
        Assert.Equal(AncestorFallbackName, entry.Event.Name);
        Assert.Equal(AncestorFallbackMessage, entry.Message);
        Assert.Equal(AncestorFallbackMessage, entry.Template);
        Assert.Equal(entry.Event, simple.EventId);
        Assert.Equal(entry.Message, simple.ToString());
    }

    /// <summary>EF's configured event level applies to both the typed delegate and the simple logger.</summary>
    [Fact]
    public void AncestorFallbackHonorsConfiguredEventLevel()
    {
        // Arrange
        var events = new List<EventData>();
        using var sink = new FallbackLoggerProvider();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(sink));
        var options = new DbContextOptionsBuilder<CompositionGuardContext>(DiagnosticOptions(events))
            .ConfigureWarnings(warnings => warnings.Log((new EventId(70_004), LogLevel.Warning)))
            .Options;

        using var provider = NestedSetTestAdapters.BuildServices(options, services => services.AddSingleton(factory));
        using var context = new CompositionGuardContext(IsolateDiagnosticOptions(options, provider));

        // Act
        NestedSetQueryDiagnostics.AncestorIndexWithoutReader(context);

        // Assert
        var logged = Assert.Single(sink.Events);
        var simple = Assert.Single(events);

        Assert.Equal(LogLevel.Warning, logged.Level);
        Assert.Equal(LogLevel.Warning, simple.LogLevel);
        Assert.Equal(logged.Event, simple.EventId);
        Assert.Equal(logged.Message, simple.ToString());
    }

    /// <summary>Captures only the library's bounded classifications through an ordinary logging provider.</summary>
    private sealed class FallbackLoggerProvider : ILoggerProvider
    {
        /// <summary>Gets the fixed diagnostic metadata and message without application state.</summary>
        internal List<(string Category, LogLevel Level, EventId Event, string Message, string? Template)> Events { get; }
            = [];

        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName) => new FallbackLogger(this, categoryName);

        /// <inheritdoc />
        public void Dispose() { }

        /// <summary>Observes ordinary ILogger dispatch without replacing EF services or warning behavior.</summary>
        private sealed class FallbackLogger(FallbackLoggerProvider owner, string category) : ILogger
        {
            /// <inheritdoc />
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            /// <inheritdoc />
            public bool IsEnabled(LogLevel logLevel) => true;

            /// <inheritdoc />
            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
            {
                if (eventId.Name?.StartsWith(QueryEventPrefix, StringComparison.Ordinal) is true)
                {
                    var template = state is IReadOnlyList<KeyValuePair<string, object?>> values
                        ? values.Single(pair => pair.Key == "{OriginalFormat}").Value as string
                        : null;

                    owner.Events.Add((category, logLevel, eventId, formatter(state, exception), template));
                }
            }
        }
    }
}

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Observes commands and actual compilation events without adding a provider singleton identity.</summary>
internal sealed class ScaleProbe : DbCommandInterceptor, IObserver<DiagnosticListener>,
    IObserver<KeyValuePair<string, object?>>, IDisposable
{
    private readonly List<IDisposable> _subscriptions = [];
    private readonly Dictionary<Guid, ScaleCommand> _readers = [];
    private DbContext? _context;

    /// <summary>Gets commands observed within the explicitly reset measurement window.</summary>
    internal List<ScaleCommand> Commands { get; } = [];

    /// <summary>Gets actual query compilations reported by EF for this context.</summary>
    internal int Compilations { get; private set; }

    /// <summary>Gets command offsets at compilation time for attributing post-write refresh queries.</summary>
    internal List<int> CompilationCommandOffsets { get; } = [];

    /// <summary>Gets readers currently open on the observed context's provider connection.</summary>
    internal int ActiveReaders { get; private set; }

    /// <summary>Starts context-filtered diagnostics after setup has completed.</summary>
    internal void Observe(DbContext context)
    {
        _context = context;
        _subscriptions.Add(DiagnosticListener.AllListeners.Subscribe(this));
    }

    /// <summary>Clears observations without changing the model, provider services, or query cache.</summary>
    internal void Reset()
    {
        Commands.Clear();
        _readers.Clear();
        Compilations = 0;
        CompilationCommandOffsets.Clear();
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        var observed = Record(command);
        _readers[eventData.CommandId] = observed;

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        Record(command);

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default
    )
    {
        ActiveReaders++;

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override InterceptionResult DataReaderDisposing(
        DbCommand command,
        DataReaderDisposingEventData eventData,
        InterceptionResult result
    )
    {
        if (_readers.TryGetValue(eventData.CommandId, out var observed))
        {
            observed.ReadCalls = eventData.ReadCount;
            ActiveReaders--;
        }

        return result;
    }

    /// <inheritdoc />
    public void OnNext(DiagnosticListener listener)
    {
        if (listener.Name == DbLoggerCategory.Name)
        {
            _subscriptions.Add(listener.Subscribe(this,
                name => name == CoreEventId.QueryCompilationStarting.Name));
        }
    }

    /// <inheritdoc />
    public void OnNext(KeyValuePair<string, object?> value)
    {
        if (value.Value is DbContextEventData data && ReferenceEquals(data.Context, _context))
        {
            Compilations++;
            CompilationCommandOffsets.Add(Commands.Count);
        }
    }

    /// <inheritdoc />
    public void OnCompleted() { }

    /// <inheritdoc />
    public void OnError(Exception error) => throw error;

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }
    }

    /// <summary>Retains SQL shape and physical parameter count without retaining provider objects.</summary>
    private ScaleCommand Record(DbCommand command)
    {
        var observed = new ScaleCommand(command.CommandText, command.Parameters.Count);
        Commands.Add(observed);

        return observed;
    }
}

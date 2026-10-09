namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Observes capacity operations with aggregate counters and sampled occupied heap.</summary>
internal sealed class CapacityProbe : DbCommandInterceptor, IAsyncDisposable
{
    private Timer? _timer;
    private readonly ManagedHeapObservation _heap = new();
    private long _baseline;
    private long _allocatedBaseline;
    private long _maximumOccupied;
    private long _samples;
    private int _measuring;

    /// <summary>Gets the number of attempted bounded repair commands.</summary>
    internal int RepairCommands { get; private set; }

    /// <summary>Gets the number of repair commands completed before a fault boundary.</summary>
    internal int CompletedRepairs { get; private set; }

    /// <summary>Gets the total rows reported by completed repair commands.</summary>
    internal long RepairedRows { get; private set; }

    /// <summary>Gets the largest repair parameter count without retaining any bindings.</summary>
    internal int MaximumRepairParameters { get; private set; }

    /// <summary>Gets the largest observed command parameter count.</summary>
    internal int MaximumCommandParameters { get; private set; }

    /// <summary>Gets the largest observed repair SQL length.</summary>
    internal int MaximumRepairSqlLength { get; private set; }

    /// <summary>Gets the number of completed public import payload saves.</summary>
    internal int CompletedSaves { get; private set; }

    /// <summary>Gets whether the late fault was injected after earlier batches completed.</summary>
    internal bool ReachedFailure { get; private set; }

    /// <summary>Gets or sets the repair command to fail before execution, or zero to disable injection.</summary>
    internal int FailRepairCommand { get; set; }

    /// <summary>Gets or sets whether to fail the final bulk result refresh.</summary>
    internal bool FailBulkRefresh { get; set; }

    /// <summary>Gets or sets a source to cancel at the selected late failure boundary.</summary>
    internal CancellationTokenSource? Cancellation { get; set; }

    /// <summary>Gets the maximum sampled occupied heap above the collected baseline.</summary>
    internal long AdditionalOccupiedBytes => Math.Max(0, Interlocked.Read(ref _maximumOccupied) - _baseline);

    /// <summary>Gets total allocation separately from the occupied-heap observation.</summary>
    internal long TotalAllocatedBytes { get; private set; }

    /// <summary>Gets the number of timer and command-boundary samples.</summary>
    internal long Samples => Interlocked.Read(ref _samples);

    /// <summary>Starts measurement only after caller-owned input and provider warmup exist.</summary>
    internal void Start()
    {
        _heap.Start();
        _baseline = _heap.Baseline;
        _maximumOccupied = _baseline;
        _allocatedBaseline = GC.GetTotalAllocatedBytes(precise: true);
        Volatile.Write(ref _measuring, 1);
        _timer = new Timer(_ => Sample(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(5));
    }

    /// <summary>Freezes observations before verification queries allocate their own result state.</summary>
    internal async Task StopAsync()
    {
        Sample();
        await DisposeAsync();
        TotalAllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - _allocatedBaseline;
    }

    /// <summary>Counts successful EF payload waves without retaining caller-owned entities.</summary>
    internal void Saved() => CompletedSaves++;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Volatile.Write(ref _measuring, 0);
        if (_timer is { } timer)
        {
            await timer.DisposeAsync();
            _timer = null;
        }
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        Observe(command);
        if (IsRepair(command))
        {
            RepairCommands++;
            MaximumRepairParameters = Math.Max(MaximumRepairParameters, command.Parameters.Count);
            MaximumRepairSqlLength = Math.Max(MaximumRepairSqlLength, command.CommandText.Length);
            if (RepairCommands == FailRepairCommand)
            {
                await FailAsync();
            }
        }

        return result;
    }

    /// <inheritdoc />
    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default
    )
    {
        Sample();
        if (IsRepair(command))
        {
            CompletedRepairs++;
            RepairedRows += result;
        }

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        Observe(command);
        if (FailBulkRefresh
            && command.CommandText.Contains(NestedSetDiagnostics.BulkRefreshTag, StringComparison.Ordinal))
        {
            await FailAsync();
        }

        return result;
    }

    /// <summary>Samples both the in-memory plan retained at first write and subsequent command boundaries.</summary>
    private void Observe(
        DbCommand command
    )
    {
        Sample();
        MaximumCommandParameters = Math.Max(MaximumCommandParameters, command.Parameters.Count);
    }

    /// <summary>Records the observed maximum without forcing collections that change operation behavior.</summary>
    private void Sample()
    {
        if (Volatile.Read(ref _measuring) == 0)
        {
            return;
        }

        var occupied = _heap.Sample();
        Interlocked.Increment(ref _samples);
        var previous = Interlocked.Read(ref _maximumOccupied);
        while (occupied > previous)
        {
            var observed = Interlocked.CompareExchange(ref _maximumOccupied, occupied, previous);
            if (observed == previous)
            {
                break;
            }

            previous = observed;
        }
    }

    /// <summary>Injects a fault or fully completed cancellation at the same late command boundary.</summary>
    private async Task FailAsync()
    {
        ReachedFailure = true;
        if (Cancellation is { } source)
        {
            await source.CancelAsync();
            source.Token.ThrowIfCancellationRequested();
        }

        throw new InjectedCommandException();
    }

    /// <summary>Identifies mapped CASE repairs without counting registry updates or setup writes.</summary>
    private static bool IsRepair(
        DbCommand command
    ) => command
            .CommandText
            .TrimStart()
            .StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
        && command.CommandText.Contains("TreeNode", StringComparison.OrdinalIgnoreCase)
        && command.CommandText.Contains("CASE", StringComparison.OrdinalIgnoreCase);
}

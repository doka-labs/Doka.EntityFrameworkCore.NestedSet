namespace Doka.EntityFrameworkCore.NestedSet.Samples;

/// <summary>Translates Ctrl+C into cooperative database cancellation for one console invocation.</summary>
internal sealed class SampleCancellation : IDisposable
{
    private readonly CancellationTokenSource _source = new();

    /// <summary>Registers a handler that lets awaited operations observe cancellation.</summary>
    public SampleCancellation()
    {
        Console.CancelKeyPress += Cancel;
    }

    /// <summary>Gets the token passed through the complete selected sample flow.</summary>
    public CancellationToken Token => _source.Token;

    /// <summary>Unregisters this invocation and releases its token source.</summary>
    public void Dispose()
    {
        Console.CancelKeyPress -= Cancel;
        _source.Dispose();
    }

    /// <summary>Requests cancellation while leaving the awaited operation responsible for orderly cleanup.</summary>
    private void Cancel(
        object? sender,
        ConsoleCancelEventArgs args
    )
    {
        args.Cancel = true;

        // WHY: Console.CancelKeyPress is a synchronous notification; cancellation is requested before it returns.
        // ReSharper disable once MethodHasAsyncOverload
        _source.Cancel();
    }
}

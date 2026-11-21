namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Proves both contexts reach a lock attempt before either is allowed to acquire its first anchor.</summary>
internal sealed class OrderingLockBarrier
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrivals;

    /// <summary>Gets the observed number of competing first-lock attempts.</summary>
    internal int Arrivals => Volatile.Read(ref _arrivals);

    /// <summary>Releases both callers only after their command interceptors have reached the lock boundary.</summary>
    internal async Task ArriveAsync(
        CancellationToken cancellationToken
    )
    {
        if (Interlocked.Increment(ref _arrivals) == 2)
        {
            _ready.SetResult();
        }

        await _ready.Task.WaitAsync(cancellationToken);
    }
}

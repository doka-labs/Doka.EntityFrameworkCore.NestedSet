namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Releases writers only after every expected participant has reached the boundary.</summary>
internal sealed class ConcurrentCapacityBarrier
{
    private readonly int _participants;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrivals;

    /// <summary>Sets the number of writers required to release the barrier.</summary>
    internal ConcurrentCapacityBarrier(
        int participants
    )
    {
        _participants = participants;
    }

    /// <summary>Gets the number of writer arrivals observed without retaining commands or entity snapshots.</summary>
    internal int Arrivals => Volatile.Read(ref _arrivals);

    /// <summary>Releases every participant after the last expected arrival.</summary>
    internal async Task ArriveAsync(
        CancellationToken cancellationToken
    )
    {
        var arrivals = Interlocked.Increment(ref _arrivals);
        Assert.InRange(arrivals, 1, _participants);

        if (arrivals == _participants)
        {
            _ready.TrySetResult();
        }

        await _ready.Task.WaitAsync(cancellationToken);
    }
}

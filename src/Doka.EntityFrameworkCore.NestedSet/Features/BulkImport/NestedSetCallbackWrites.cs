namespace Doka.EntityFrameworkCore.NestedSet.Features.BulkImport;

/// <summary>
/// Retains application writes accepted by library-owned insertion saves until the operation completes.
/// </summary>
internal sealed class NestedSetCallbackWrites
{
    private List<NestedSetTrackerSnapshot>? _snapshots;

    /// <summary>Records one save's pre-persistence callback writes in persistence order.</summary>
    /// <param name="snapshot">The captured writes, or null when callbacks added no ordinary write.</param>
    internal void Add(
        NestedSetTrackerSnapshot? snapshot
    )
    {
        if (snapshot is not null)
        {
            (_snapshots ??= []).Add(snapshot);
        }
    }

    /// <summary>Restores every recorded write to its pending state after the owning transaction rolled back.</summary>
    /// <exception cref="AggregateException">One or more entries could not be restored; discard the context.</exception>
    internal void Restore()
    {
        if (_snapshots is null)
        {
            return;
        }

        List<Exception>? errors = null;

        // WHY: A later batch can modify an entry that an earlier batch already accepted. Restoring newest first
        // leaves the earliest pending original values, which match the rolled-back database row.
        for (var index = _snapshots.Count - 1; index >= 0; index--)
        {
            try
            {
                _snapshots[index]
                    .Restore();
            }
            catch (Exception error)
            {
                (errors ??= []).Add(error);
            }
        }

        if (errors is not null)
        {
            throw new AggregateException("One or more save callback writes could not be restored.", errors);
        }
    }
}

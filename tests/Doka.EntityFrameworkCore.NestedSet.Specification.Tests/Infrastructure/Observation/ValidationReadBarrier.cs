namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Commits a competing mutation between the two structural reads of string-key validation.</summary>
internal sealed class ValidationReadBarrier : DbCommandInterceptor
{
    private readonly Func<CancellationToken, Task> _betweenReads;

    /// <summary>Creates a deterministic callback that runs after the first structural result has been consumed.</summary>
    internal ValidationReadBarrier(
        Func<CancellationToken, Task> betweenReads
    )
    {
        _betweenReads = betweenReads;
    }

    /// <summary>Gets the number of hierarchy SELECT statements observed by this context.</summary>
    internal int Reads { get; private set; }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        if (command
                .CommandText
                .TrimStart()
                .StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            && command.CommandText.Contains("TextNode", StringComparison.Ordinal))
        {
            Reads++;
            if (Reads == 2)
            {
                // WHY: The writer must finish before parent links are queried; timing-based sleeps cannot prove
                // that both validation reads belong to one MVCC snapshot under an actual concurrent commit.
                await _betweenReads(cancellationToken);
            }
        }

        return result;
    }
}

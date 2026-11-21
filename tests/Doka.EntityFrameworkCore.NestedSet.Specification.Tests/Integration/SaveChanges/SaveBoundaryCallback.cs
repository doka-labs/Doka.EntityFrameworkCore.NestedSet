namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Changes the tracked graph at the same interceptor boundary used by application save callbacks.</summary>
public sealed class SaveBoundaryCallback : SaveChangesInterceptor
{
    /// <summary>Gets or sets the callback for one save; pooled reuse explicitly clears it between leases.</summary>
    public Action<DbContext>? OnSaving { get; set; }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result
    )
    {
        OnSaving?.Invoke(eventData.Context!);

        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        OnSaving?.Invoke(eventData.Context!);

        return ValueTask.FromResult(result);
    }
}

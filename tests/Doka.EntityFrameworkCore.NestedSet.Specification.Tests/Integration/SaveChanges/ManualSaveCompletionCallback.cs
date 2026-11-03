namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Introduces application edits after the persistence guard has completed a zero-write save.</summary>
internal sealed class ManualSaveCompletionCallback : SaveChangesInterceptor
{
    /// <summary>Gets or sets the application callback invoked after successful persistence.</summary>
    internal Action? OnSaved { get; set; }

    /// <inheritdoc />
    public override int SavedChanges(
        SaveChangesCompletedEventData eventData,
        int result
    )
    {
        OnSaved?.Invoke();

        return result;
    }

    /// <inheritdoc />
    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default
    )
    {
        OnSaved?.Invoke();

        return ValueTask.FromResult(result);
    }
}

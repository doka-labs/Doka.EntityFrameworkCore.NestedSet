namespace Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

/// <summary>Rejects uncoordinated hierarchy writes before Entity Framework Core sends database commands.</summary>
internal sealed class NestedSetSaveGuardInterceptor : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result
    )
    {
        if (eventData.Context is { } context)
        {
            NestedSetSaveChanges.RequireSafeSave(context);
        }

        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (eventData.Context is { } context)
        {
            NestedSetSaveChanges.RequireSafeSave(context);
        }

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override int SavedChanges(
        SaveChangesCompletedEventData eventData,
        int result
    )
    {
        VerifyEmptySave(eventData.Context, result);

        return result;
    }

    /// <inheritdoc />
    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default
    )
    {
        VerifyEmptySave(eventData.Context, result);

        return ValueTask.FromResult(result);
    }

    /// <summary>Checks late pre-save edits when EF's zero-write fast path never reaches the database service.</summary>
    private static void VerifyEmptySave(
        DbContext? context,
        int result
    )
    {
        if (result == 0
            && context is not null)
        {
            // WHY: Injected completion interceptors run before application completion callbacks. Validate the
            // finished no-write save here, without mistaking later SavedChanges edits for pre-save hierarchy work.
            NestedSetSaveChanges.RequireSafeSave(context);
        }
    }
}

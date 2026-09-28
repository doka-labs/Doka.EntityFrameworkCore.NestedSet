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
}

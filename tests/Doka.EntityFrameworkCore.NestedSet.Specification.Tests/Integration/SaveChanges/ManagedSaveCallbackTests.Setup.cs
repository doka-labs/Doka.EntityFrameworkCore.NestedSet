namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class ManagedSaveCallbackTests
{
    /// <summary>Seeds and tracks callback writes shared by the rollback and retry checks.</summary>
    private static async Task<(UnrelatedRow Changed, UnrelatedRow Removed, GeneratedAuditRow Audit)>
        PrepareCallbackWritesAsync(
            TestDatabase database,
            TreeContext context,
            SaveBoundaryCallback callback,
            CancellationToken cancellationToken
        )
    {
        await using (var setup = database.CreateContext())
        {
            await setup.AddRangeAsync(
                [
                    new UnrelatedRow
                    {
                        Id = 701,
                        Value = "stored",
                    },
                    new UnrelatedRow
                    {
                        Id = 702,
                        Value = "removed",
                    },
                ],
                cancellationToken);

            await setup.SaveChangesAsync(cancellationToken);
        }

        var changed = await context
            .Set<UnrelatedRow>()
            .SingleAsync(row => row.Id == 701, cancellationToken);

        var removed = await context
            .Set<UnrelatedRow>()
            .SingleAsync(row => row.Id == 702, cancellationToken);

        var audit = new GeneratedAuditRow { Value = "inserted" };
        callback.OnSaving = current =>
        {
            // WHY: This synchronous SavingChanges callback must finish attaching the audit before it returns.
            // The configured identity generator does not query the database during Add.
            // ReSharper disable once MethodHasAsyncOverload
            current.Add(audit);
            changed.Value = "changed";
            current.Remove(removed);
        };

        return (changed, removed, audit);
    }
}

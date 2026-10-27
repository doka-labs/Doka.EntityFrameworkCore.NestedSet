namespace Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

internal static partial class NestedSetSaveChanges
{
    /// <summary>Checks a managed insertion's exact write set at EF's persistence boundary.</summary>
    /// <param name="context">The context whose save is about to send its first command.</param>
    /// <param name="entries">The complete pending write set passed to Entity Framework Core's database service.</param>
    /// <exception cref="NestedSetException">
    /// Save callbacks changed, removed, or extended the planned hierarchy.
    /// </exception>
    internal static void ValidateManagedPersistence(
        DbContext context,
        IList<IUpdateEntry> entries
    )
    {
        if (!s_contexts.TryGetValue(context, out var state)
            || state is not { ManagedDepth: > 0, ManagedValidation: { } validation, ManagedEntities: { } planned })
        {
            return;
        }

        var tracker = context.ChangeTracker;
        var autoDetectChanges = tracker.AutoDetectChangesEnabled;

        try
        {
            // WHY: EF already detected this final write set. Validators that enumerate tracked entries must not
            // start another tracker-wide detection inside the persistence step.
            tracker.AutoDetectChangesEnabled = false;
            ValidateManagedWriteSet(context, state, validation, planned, entries);
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = autoDetectChanges;
        }
    }

    /// <summary>Validates one managed write set and captures the ordinary writes that accompany it.</summary>
    /// <param name="context">The context whose save is about to send its first command.</param>
    /// <param name="state">The context-local plan updated with the captured writes.</param>
    /// <param name="validation">The operation's structural validation for its staged entries.</param>
    /// <param name="planned">The exact entity references planned for this insertion save.</param>
    /// <param name="entries">The complete pending write set passed to Entity Framework Core's database service.</param>
    private static void ValidateManagedWriteSet(
        DbContext context,
        SaveState state,
        Action validation,
        HashSet<object> planned,
        IList<IUpdateEntry> entries
    )
    {
        validation();
        var model = NestedSetModelMapping.For(context.Model);
        List<IUpdateEntry>? callbackWrites = null;
        var plannedCount = 0;

        // WHY: EF passes only pending writes. Validating this list avoids a tracker-wide scan and cannot observe
        // later SavedChanges or post-save detection, which run after EF has accepted the inserted entries.
        foreach (var entry in entries)
        {
            var entity = entry.ToEntityEntry()
                .Entity;

            if (planned.Contains(entity))
            {
                if (entry.EntityState != EntityState.Added)
                {
                    throw new NestedSetException(
                        NestedSetErrorCode.InvalidContext,
                        "Save callbacks must retain every planned insertion until persistence begins.");
                }

                plannedCount++;

                continue;
            }

            if (model.TryDescriptor(entry.EntityType, out _))
            {
                // WHY: An unaffected tracked node may carry application payload through the same EF save. Only
                // hierarchy-owned or configured ordering properties need the insertion's structural lock plan.
                var ordering = model.Ordering(entry.EntityType);
                var changesHierarchy = entry.EntityState != EntityState.Modified
                    || model
                        .StructuralProperties(entry.EntityType)
                        .Any(entry.IsModified)
                    || (ordering is not null && ordering.Properties.Any(entry.IsModified));

                if (changesHierarchy)
                {
                    throw new NestedSetException(
                        NestedSetErrorCode.InvalidContext,
                        "Save callbacks must not introduce hierarchy or ordering writes "
                        + "outside the planned insertion.");
                }
            }

            // WHY: Application payload, audit, and outbox writes persist atomically with the insertion. EF may
            // accept them before a later step fails, so their pending state is restored after rollback.
            (callbackWrites ??= []).Add(entry);
        }

        if (plannedCount != planned.Count)
        {
            throw new NestedSetException(
                NestedSetErrorCode.InvalidContext,
                "Save callbacks must not detach planned hierarchy insertions.");
        }

        // WHY: Every SavingChanges callback has completed before this boundary, so the snapshot holds their final
        // pending values, including temporary keys that EF replaces during persistence.
        state.ManagedCallbackWrites = callbackWrites is null
            ? null
            : NestedSetTrackerSnapshot.Capture(context, callbackWrites);

        state.ManagedPersistenceReached = true;
    }

    /// <summary>Guards a library-owned insertion against callback-created work before any SQL is sent.</summary>
    /// <param name="context">The context whose mutation already owns its lock and rollback boundary.</param>
    /// <param name="entities">The exact hierarchy entities planned for the insertion save.</param>
    /// <param name="validation">The structural validation executed after save callbacks and change detection.</param>
    /// <param name="persistenceCompleted">Bounded identity bookkeeping after results and before EF acceptance.</param>
    /// <returns>A scope that restores the previous context-local managed-save plan.</returns>
    /// <exception cref="NestedSetException">The context was not configured through UseNestedSets.</exception>
    internal static ManagedSaveScope EnterManagedSave(
        DbContext context,
        IEnumerable<object> entities,
        Action validation,
        Action? persistenceCompleted = null
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(validation);

        // WHY: Without the persistence hook, callback writes would be accepted without validation or a rollback
        // snapshot. Reject before the first managed save instead of detecting the gap after SQL.
        if (context.GetService<IDatabase>() is not NestedSetRelationalDatabase)
        {
            throw new NestedSetException(
                NestedSetErrorCode.InvalidContext,
                "Nested-set insertions require UseNestedSets() inside the matching native provider callback "
                + "to install the persistence guard.");
        }

        var state = s_contexts.GetValue(context, static _ => new SaveState());
        var scope = new ManagedSaveScope(state, context.ChangeTracker);
        state.ManagedEntities = entities.ToHashSet(ReferenceEqualityComparer.Instance);
        state.ManagedValidation = validation;
        state.ManagedPersistenceCompleted = persistenceCompleted;
        state.ManagedCallbackWrites = null;
        state.ManagedPersistenceReached = false;
        state.ManagedDepth++;

        // WHY: EF's final detection must observe CLR edits made by save callbacks even when the application
        // normally detects changes manually; the persistence hook validates that detected write set.
        context.ChangeTracker.AutoDetectChangesEnabled = true;

        return scope;
    }

    /// <summary>Gets bounded insertion bookkeeping at the provider-result boundary, or no ordinary-save work.</summary>
    /// <param name="context">The exact context whose database service is completing the save.</param>
    /// <returns>The active callback, or null when this save does not own insertion identity snapshots.</returns>
    internal static Action? ManagedPersistenceCompletion(
        DbContext context
    ) => s_contexts.TryGetValue(context, out var state) && state.ManagedDepth > 0
        ? state.ManagedPersistenceCompleted
        : null;

    /// <summary>Allows the coordinated save to reuse the exact-tree mutation engine after its payload save.</summary>
    /// <param name="context">The context whose outer save owns the complete transaction and lock plan.</param>
    /// <returns>A scope that restores the internal mutation state on disposal.</returns>
    internal static IDisposable EnterManagedMutation(
        DbContext context
    )
    {
        var state = s_contexts.GetValue(context, static _ => new SaveState());
        state.ManagedMutationDepth++;

        return new ManagedMutation(state);
    }

    /// <summary>Gets whether the coordinated save currently applies its preplanned Parent moves.</summary>
    /// <param name="context">The context checked without accessing provider services.</param>
    /// <returns>Whether the exact internal mutation sub-boundary is active.</returns>
    internal static bool IsManagedMutation(
        DbContext context
    ) => s_contexts.TryGetValue(context, out var state) && state.ManagedMutationDepth > 0;
}

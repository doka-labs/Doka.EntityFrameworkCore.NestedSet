namespace Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

internal static partial class NestedSetSaveChanges
{
    /// <summary>Rejects callback-created hierarchy work after EF detection and before persistence begins.</summary>
    /// <param name="context">The context whose ordinary base save remains responsible for persistence.</param>
    /// <param name="saveChanges">The base save delegate, always called with acceptance disabled.</param>
    /// <param name="planned">The exact scalar plan, or null when no hierarchy coordination is needed.</param>
    /// <param name="snapshot">The outer rollback owner, absent for an ordinary EF save.</param>
    /// <param name="cancellationToken">The token forwarded unchanged to EF Core.</param>
    /// <returns>The saved-entry count reported by the underlying provider.</returns>
    private static async Task<int> SaveGuardedAsync(
        DbContext context,
        Func<CancellationToken, Task<int>> saveChanges,
        Dictionary<IUpdateEntry, CandidatePlan>? planned,
        NestedSetTrackerSnapshot? snapshot,
        CancellationToken cancellationToken
    )
    {
        var tracker = context.ChangeTracker;
        var autoDetectChanges = tracker.AutoDetectChangesEnabled;
        var state = s_contexts.GetValue(context, static _ => new SaveState());
        var previousValidation = state.SaveValidation;
        var previousCompletion = state.SavePersistenceCompleted;

        void VerifyCandidates()
        {
            if (HasHierarchyChanges(context, planned))
            {
                throw new NestedSetException(
                    NestedSetErrorCode.InvalidContext,
                    "Save callbacks must not introduce or alter hierarchy changes after planning.");
            }
        }

        // WHY: Raw hierarchy values are checked independently of EF's flags. Enabling automatic detection here
        // would also discover unrelated application payload that the caller deliberately left unmarked.
        state.SaveValidation = VerifyCandidates;
        state.SavePersistenceCompleted = snapshot is null
            ? null
            : snapshot.CapturePersistedRelationships;

        try
        {
            return await saveChanges(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            // WHY: Pooled contexts must never retain a previous lease's guard or altered detection setting.
            tracker.AutoDetectChangesEnabled = autoDetectChanges;
            state.SaveValidation = previousValidation;
            state.SavePersistenceCompleted = previousCompletion;
        }
    }

    /// <summary>Captures intended guarded scalars before Parent suppression for the original candidates.</summary>
    /// <param name="candidates">Candidates whose physical Parent is captured again after suppression.</param>
    /// <returns>A reference-identity map used only through the one underlying save callback boundary.</returns>
    private static Dictionary<IUpdateEntry, CandidatePlan> CaptureGuardPlan(
        List<EntityEntry> candidates
    )
    {
        var result = new Dictionary<IUpdateEntry, CandidatePlan>(candidates.Count, ReferenceEqualityComparer.Instance);

        foreach (var entry in candidates)
        {
            var update = NestedSetInsertionContract.Instance.Entry(entry);

            result.Add(update, CandidatePlan.Capture(update, entry.Entity));
        }

        return result;
    }

    /// <summary>Requires options registration before an ordered hierarchy service can be used.</summary>
    /// <param name="context">The context checked without touching its model or connection.</param>
    internal static void RequireConfigured(
        DbContext context
    )
    {
        var options = context.GetService<IDbContextOptions>();
        if (options.FindExtension<NestedSetOptionsExtension>() is null)
        {
            throw new NestedSetException(
                NestedSetErrorCode.InvalidContext,
                "Nested-set integration requires UseNestedSets() inside the matching native provider callback "
                + "and the coordinated "
                + "SaveNestedSetChangesAsync wrapper for tracked hierarchy changes.");
        }
    }

    /// <summary>Rejects hierarchy writes that bypass the coordinated save wrapper.</summary>
    /// <param name="context">The context whose save is about to persist tracked changes.</param>
    internal static void RequireSafeSave(
        DbContext context
    )
    {
        if (s_contexts.TryGetValue(context, out var state)
            && state.Active)
        {
            state.SaveValidation?.Invoke();

            return;
        }

        if (state is { ManagedDepth: > 0, ManagedValidation: not null, ManagedEntities: not null })
        {
            // WHY: Later SavingChanges callbacks can still change the plan. The persistence hook validates EF's
            // final write set after every callback and change detection.
            return;
        }

        if (!NestedSetModelMapping.For(context.Model)
            .HasHierarchies)
        {
            return;
        }

        // WHY: Guarding hierarchy roles must not mark unrelated CLR payload as modified when the application
        // disabled automatic detection. Local EntityEntry.DetectChanges also walks tracked FK principals.
        if (HasHierarchyChanges(context))
        {
            throw new NestedSetException(
                NestedSetErrorCode.InvalidContext,
                "Use nested-set facade operations to insert/delete nodes or change hierarchy-owned structure. "
                + "Tracked Parent/order edits require the SaveNestedSetChangesAsync wrapper.");
        }
    }

    /// <summary>Validates managed structure and finds saved domain properties that change sibling order.</summary>
    /// <param name="context">The context whose writes were explicitly marked or automatically detected.</param>
    /// <returns>Modified entries with at least one configured ordering property marked modified.</returns>
    private static List<EntityEntry> FindCandidates(
        DbContext context
    )
    {
        var result = new List<EntityEntry>();

        var model = NestedSetModelMapping.For(context.Model);
        var updates = context
            .GetService<IUpdateAdapterFactory>()
            .Create();

        // WHY: Planning uses only known writes. The adapter exposes flags without another detection pass,
        // an array of every tracked entity, or PropertyEntry wrappers for unrelated application payload.
        foreach (var entry in updates.Entries)
        {
            if (!model.TryDescriptor(entry.EntityType, out var descriptor))
            {
                continue;
            }

            var ordering = model.Ordering(entry.EntityType);

            if (entry.EntityState is EntityState.Added or EntityState.Deleted)
            {
                throw new NestedSetException(
                    NestedSetErrorCode.InvalidContext,
                    "Use nested-set facade operations to insert or delete hierarchy nodes.");
            }

            var parent = descriptor.Parent.Resolve(entry.EntityType);
            var hasDirectStructureChange = model
                .StructuralProperties(entry.EntityType)
                .Any(property => property != parent && entry.IsModified(property));

            if (hasDirectStructureChange)
            {
                throw new NestedSetException(
                    NestedSetErrorCode.InvalidContext,
                    "Use nested-set facade operations to change hierarchy-owned structure.");
            }

            if (entry.EntityState == EntityState.Modified
                && (entry.IsModified(parent) || (ordering is not null && ordering.Properties.Any(entry.IsModified))))
            {
                result.Add(entry.ToEntityEntry());
            }
        }

        return result;
    }
}

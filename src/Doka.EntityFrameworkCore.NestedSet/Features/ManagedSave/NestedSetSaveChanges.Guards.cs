namespace Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

internal static partial class NestedSetSaveChanges
{
    /// <summary>Rejects callback-created hierarchy work after EF detection and before persistence begins.</summary>
    /// <param name="context">The context whose ordinary base save remains responsible for persistence.</param>
    /// <param name="saveChanges">The base save delegate, always called with acceptance disabled.</param>
    /// <param name="planned">The exact trigger-property plan, or null when no hierarchy coordination is needed.</param>
    /// <param name="cancellationToken">The token forwarded unchanged to EF Core.</param>
    /// <returns>The saved-entry count reported by the underlying provider.</returns>
    private static async Task<int> SaveGuardedAsync(
        DbContext context,
        Func<CancellationToken, Task<int>> saveChanges,
        Dictionary<object, CandidatePlan>? planned,
        CancellationToken cancellationToken
    )
    {
        var tracker = context.ChangeTracker;
        var autoDetectChanges = tracker.AutoDetectChangesEnabled;

        void VerifyCandidates(
            object? sender,
            DetectedChangesEventArgs arguments
        )
        {
            var candidates = FindCandidates(context);
            var changed = candidates.Count != (planned?.Count ?? 0)
                || candidates.Any(entry => planned is null
                    || !planned.TryGetValue(entry.Entity, out var candidate)
                    || !candidate.Matches(entry));

            if (changed)
            {
                throw new NestedSetException(
                    NestedSetErrorCode.InvalidContext,
                    "Save callbacks must not introduce or alter hierarchy changes after planning.");
            }
        }

        // WHY: EF runs detection after SavingChanges events and interceptors, before any SQL. Enabling it for
        // this boundary preserves that safety check when the caller normally performs manual change detection.
        tracker.DetectedAllChanges += VerifyCandidates;
        tracker.AutoDetectChangesEnabled = true;

        try
        {
            return await saveChanges(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            // WHY: Pooled contexts must never retain a previous lease's guard or altered detection setting.
            tracker.DetectedAllChanges -= VerifyCandidates;
            tracker.AutoDetectChangesEnabled = autoDetectChanges;
        }
    }

    /// <summary>Captures exact modified trigger properties and values after Parent suppression.</summary>
    /// <param name="context">The context whose detected hierarchy plan is already locked.</param>
    /// <returns>A reference-identity map used only through the one underlying save callback boundary.</returns>
    private static Dictionary<object, CandidatePlan> CaptureGuardPlan(
        DbContext context
    )
    {
        var result = new Dictionary<object, CandidatePlan>(ReferenceEqualityComparer.Instance);
        var model = NestedSetModelMapping.For(context.Model);

        foreach (var entry in FindCandidates(context))
        {
            var descriptor = model.Descriptor(entry.Metadata);
            var parent = descriptor.Parent.Resolve(entry.Metadata);
            var ordering = model.Ordering(entry.Metadata);
            var triggers = ordering is null
                ? [parent]
                : new[] { parent }
                    .Concat(ordering.Properties)
                    .ToArray();

            result.Add(entry.Entity, CandidatePlan.Capture(entry, triggers));
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

        context.ChangeTracker.DetectChanges();

        if (FindCandidates(context).Count > 0)
        {
            throw new NestedSetException(
                NestedSetErrorCode.InvalidContext,
                "Tracked hierarchy changes require the SaveNestedSetChangesAsync wrapper.");
        }
    }

    /// <summary>Validates managed structure and finds saved domain properties that change sibling order.</summary>
    /// <param name="context">The context whose change detection has already run.</param>
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

        // WHY: Planning already detected changes. The adapter exposes flags without another detection pass,
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

    /// <summary>Freezes modified trigger metadata and provider-aware snapshots across save callbacks.</summary>
    private sealed class CandidatePlan
    {
        private readonly IReadOnlyDictionary<IProperty, object?> _values;

        /// <summary>Creates one immutable trigger plan.</summary>
        /// <param name="values">Modified properties and their safely snapshotted current values.</param>
        private CandidatePlan(
            IReadOnlyDictionary<IProperty, object?> values
        )
        {
            _values = values;
        }

        /// <summary>Captures only trigger properties that are currently marked modified.</summary>
        internal static CandidatePlan Capture(
            EntityEntry entry,
            IReadOnlyList<IProperty> triggers
        )
        {
            var values = new Dictionary<IProperty, object?>();

            foreach (var property in triggers)
            {
                var candidate = entry.Property(property);

                if (candidate.IsModified)
                {
                    values.Add(
                        property,
                        property
                            .GetValueComparer()
                            .Snapshot(candidate.CurrentValue));
                }
            }

            return new CandidatePlan(values);
        }

        /// <summary>Checks that callbacks retained the same modified triggers and exact provider values.</summary>
        internal bool Matches(
            EntityEntry entry
        )
        {
            var model = NestedSetModelMapping.For(entry.Context.Model);
            var descriptor = model.Descriptor(entry.Metadata);
            var parent = descriptor.Parent.Resolve(entry.Metadata);
            var ordering = model.Ordering(entry.Metadata);
            var triggers = ordering is null ? new[] { parent } : new[] { parent }.Concat(ordering.Properties);

            foreach (var property in triggers)
            {
                var candidate = entry.Property(property);
                var planned = _values.TryGetValue(property, out var value);

                if (candidate.IsModified != planned
                    || (planned
                        && !property
                            .GetValueComparer()
                            .Equals(candidate.CurrentValue, value)))
                {
                    return false;
                }
            }

            return true;
        }
    }
}

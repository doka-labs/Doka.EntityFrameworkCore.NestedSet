namespace Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

/// <summary>Implements the context-local save coordinator behind the public extension methods.</summary>
internal static class NestedSetSaveChanges
{
    private static readonly ConditionalWeakTable<DbContext, SaveState> s_contexts = new();

    /// <summary>Saves explicitly synchronously while rejecting hierarchy changes before persistence.</summary>
    /// <param name="context">The context registered through <c>UseNestedSets()</c>.</param>
    /// <param name="saveChanges">A delegate calling base.SaveChanges with the caller's acceptance flag.</param>
    /// <returns>The unchanged EF Core saved-entry count.</returns>
    /// <remarks>
    ///     Use this only from an explicitly synchronous override. Ordered hierarchy changes require
    ///     <see cref="ExecuteAsync"/>. For hierarchy models, automatic detection is enabled during the base save so
    ///     callbacks cannot introduce an unchecked hierarchy write; its original setting is restored afterward.
    ///     Callbacks must not disable detection, detach entries, accept changes, or recursively save. Models without
    ///     ordering retain the caller's native detection setting.
    /// </remarks>
    /// <exception cref="NestedSetException">
    ///     Integration is missing or hierarchy changes require an async save.
    /// </exception>
    internal static int ExecuteSynchronous(
        DbContext context,
        Func<int> saveChanges
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(saveChanges);
        RequireConfigured(context);
        var state = s_contexts.GetValue(context, static _ => new SaveState());

        if (state.Active
            || state.ManagedDepth > 0)
        {
            throw new NestedSetException(
                NestedSetErrorCode.InvalidContext,
                "Synchronous saves must not enter an active nested-set save boundary.");
        }

        if (!NestedSetModelMapping.For(context.Model)
                .HasHierarchies)
        {
            // WHY: Without hierarchy entities, no callback can introduce a structural write.
            return saveChanges();
        }

        var tracker = context.ChangeTracker;
        var autoDetectChanges = tracker.AutoDetectChangesEnabled;

        void VerifyCandidates(
            object? sender,
            DetectedChangesEventArgs arguments
        )
        {
            if (FindCandidates(context)
                    .Count
                > 0)
            {
                throw new NestedSetException(
                    NestedSetErrorCode.InvalidContext,
                    "Hierarchy changes require the coordinated SaveChangesAsync override.");
            }
        }

        // WHY: This explicit synchronous path must validate after callbacks, just before EF's native SQL boundary.
        state.Active = true;
        tracker.DetectedAllChanges += VerifyCandidates;
        tracker.AutoDetectChangesEnabled = true;

        try
        {
            return saveChanges();
        }
        finally
        {
            tracker.DetectedAllChanges -= VerifyCandidates;
            tracker.AutoDetectChangesEnabled = autoDetectChanges;
            state.Active = false;
        }
    }

    /// <summary>Saves payload and uses a transaction or savepoint when configured sibling order needs repair.</summary>
    /// <param name="context">The configured caller-owned context.</param>
    /// <param name="acceptAllChangesOnSuccess">Whether to accept changes after the complete boundary succeeds.</param>
    /// <param name="saveChanges">
    ///     A delegate that calls base.SaveChangesAsync(false, token). It must not recurse into the override, accept
    ///     changes, mutate hierarchy entries, disable change detection, or detach entries. The coordinator invokes it
    ///     exactly once, after locking when hierarchy ordering requires coordination.
    /// </param>
    /// <param name="cancellationToken">The token used for forward progress; rollback cleanup is not canceled.</param>
    /// <returns>The unchanged EF Core saved-entry count, excluding structural range updates.</returns>
    /// <remarks>
    ///     Invoke this method through the public save extension from the asynchronous save override. Payload originals
    ///     remain pending when acceptance is false; managed structure is synchronized
    ///     with the database. Failed coordinated boundaries restore the pre-save tracker snapshot. Commit failures can
    ///     have an ambiguous database outcome and must not be retried blindly. Caller transactions retain commit
    ///     ownership.
    ///     Saves without ordering candidates use EF Core's normal transaction and execution strategy. Change detection
    ///     runs before planning and after callbacks in ordered models even if automatic detection was disabled;
    ///     the caller's setting is restored afterward. Models without ordering retain native detection behavior.
    ///     Callbacks must not disable detection in an ordered model or introduce additional hierarchy changes.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///     Integration, tracking, or transaction requirements are violated.
    /// </exception>
    internal static async Task<int> ExecuteAsync(
        DbContext context,
        bool acceptAllChangesOnSuccess,
        Func<CancellationToken, Task<int>> saveChanges,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(saveChanges);
        cancellationToken.ThrowIfCancellationRequested();
        RequireConfigured(context);
        var state = s_contexts.GetValue(context, static _ => new SaveState());

        if (state.ManagedDepth > 0)
        {
            var managedCount = await saveChanges(cancellationToken).ConfigureAwait(false);

            if (acceptAllChangesOnSuccess)
            {
                context.ChangeTracker.AcceptAllChanges();
            }

            return managedCount;
        }

        if (state.Active)
        {
            throw new NestedSetException(
                NestedSetErrorCode.InvalidContext,
                "Nested-set SaveChanges integration must not invoke itself recursively.");
        }

        if (!NestedSetModelMapping.For(context.Model)
                .HasHierarchies)
        {
            // WHY: Models without hierarchies retain EF's own detection setting and avoid all hierarchy scans.
            var count = await saveChanges(cancellationToken).ConfigureAwait(false);

            if (acceptAllChangesOnSuccess)
            {
                context.ChangeTracker.AcceptAllChanges();
            }

            return count;
        }

        context.ChangeTracker.DetectChanges();
        var candidates = FindCandidates(context);

        if (candidates.Count == 0)
        {
            state.Active = true;

            try
            {
                // WHY: A sorted entity elsewhere in the model must not change retries, ambient transactions,
                // isolation, or allocations for an unrelated save. The temporary guard runs before EF sends SQL.
                var ordinaryCount = await SaveGuardedAsync(context, saveChanges, null, cancellationToken)
                    .ConfigureAwait(false);

                if (acceptAllChangesOnSuccess)
                {
                    context.ChangeTracker.AcceptAllChanges();
                }

                return ordinaryCount;
            }
            finally
            {
                state.Active = false;
            }
        }

        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        var groups = NestedSetSaveGroup.Create(context, candidates);
        var savedCount = 0;
        state.Active = true;

        try
        {
            await NestedSetTelemetry
                .ExecuteAsync(
                    context,
                    "save_changes",
                    boundaryToken => NestedSetTransaction.ExecuteAsync(
                        context,
                        async token =>
                        {
                            var requests = new List<INestedSetTreeLockRequest>();

                            foreach (var group in groups)
                            {
                                await group
                                    .ResolveScopesAsync(requests, token)
                                    .ConfigureAwait(false);
                            }

                            await NestedSetSaveLocks
                                .AcquireAsync(context, requests, token)
                                .ConfigureAwait(false);

                            foreach (var group in groups)
                            {
                                await group
                                    .PrepareLockedChangesAsync(token)
                                    .ConfigureAwait(false);
                            }

                            foreach (var group in groups)
                            {
                                group.SuppressParentChanges();
                            }

                            try
                            {
                                var guardPlan = CaptureGuardPlan(context);

                                savedCount = await SaveGuardedAsync(context, saveChanges, guardPlan, token)
                                    .ConfigureAwait(false);
                            }
                            finally
                            {
                                foreach (var group in groups)
                                {
                                    group.RestoreParentChanges();
                                }
                            }

                            foreach (var group in groups)
                            {
                                await group
                                    .ApplyParentChangesAsync(token)
                                    .ConfigureAwait(false);
                            }

                            foreach (var group in groups)
                            {
                                await group
                                    .ReorderAsync(token)
                                    .ConfigureAwait(false);
                            }

                            foreach (var group in groups)
                            {
                                await group
                                    .RefreshAsync(token)
                                    .ConfigureAwait(false);
                            }
                        },
                        boundaryToken),
                    cancellationToken)
                .ConfigureAwait(false);

            if (acceptAllChangesOnSuccess)
            {
                context.ChangeTracker.AcceptAllChanges();
            }

            return savedCount;
        }
        catch (Exception saveError)
        {
            try
            {
                snapshot.Restore();
            }
            catch (Exception restoreError)
            {
                throw new AggregateException(
                    "Nested-set tracker restoration failed. Discard the context.",
                    saveError,
                    restoreError);
            }

            throw;
        }
        finally
        {
            state.Active = false;
        }
    }

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
            return await saveChanges(cancellationToken).ConfigureAwait(false);
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
                ? new[] { parent }
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
                "Nested-set integration requires optionsBuilder.UseNestedSets() and the coordinated "
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

        if (FindCandidates(context)
                .Count
            > 0)
        {
            throw new NestedSetException(
                NestedSetErrorCode.InvalidContext,
                "Tracked hierarchy changes require the SaveNestedSetChangesAsync wrapper.");
        }
    }

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
    /// <returns>A scope that restores the previous context-local managed-save plan.</returns>
    /// <exception cref="NestedSetException">The context was not configured through UseNestedSets.</exception>
    internal static ManagedSaveScope EnterManagedSave(
        DbContext context,
        IEnumerable<object> entities,
        Action validation
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
                "Nested-set insertions require optionsBuilder.UseNestedSets() to install the persistence guard.");
        }

        var state = s_contexts.GetValue(context, static _ => new SaveState());
        var scope = new ManagedSaveScope(state, context.ChangeTracker);
        state.ManagedEntities = entities.ToHashSet(ReferenceEqualityComparer.Instance);
        state.ManagedValidation = validation;
        state.ManagedCallbackWrites = null;
        state.ManagedPersistenceReached = false;
        state.ManagedDepth++;

        // WHY: EF's final detection must observe CLR edits made by save callbacks even when the application
        // normally detects changes manually; the persistence hook validates that detected write set.
        context.ChangeTracker.AutoDetectChangesEnabled = true;

        return scope;
    }

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
                && (entry.IsModified(parent) || ordering is not null && ordering.Properties.Any(entry.IsModified)))
            {
                result.Add(entry.ToEntityEntry());
            }
        }

        return result;
    }

    /// <summary>Tracks only context integration and bounded reentry; it never owns transaction resources.</summary>
    internal sealed class SaveState
    {
        /// <summary>Marks the one underlying save inside a guarded execution boundary.</summary>
        internal bool Active { get; set; }

        /// <summary>Counts bounded internal insertion saves that already own their write protocol.</summary>
        internal int ManagedDepth { get; set; }

        /// <summary>Gets or sets the exact entity references allowed through the current insertion save.</summary>
        internal HashSet<object>? ManagedEntities { get; set; }

        /// <summary>Gets or sets the post-callback structural validation for the current insertion.</summary>
        internal Action? ManagedValidation { get; set; }

        /// <summary>Marks that EF passed the current plan's validated write set to its persistence step.</summary>
        internal bool ManagedPersistenceReached { get; set; }

        /// <summary>Gets or sets pending application writes captured at the plan's persistence step.</summary>
        internal NestedSetTrackerSnapshot? ManagedCallbackWrites { get; set; }

        /// <summary>Counts preplanned Parent moves executed inside the coordinated save transaction.</summary>
        internal int ManagedMutationDepth { get; set; }
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

    /// <summary>Owns one library-owned insertion save and restores the internal-save bypass even on failure.</summary>
    internal sealed class ManagedSaveScope : IDisposable
    {
        private readonly SaveState _state;
        private readonly ChangeTracker _tracker;
        private readonly bool _autoDetectChanges;
        private readonly HashSet<object>? _previousEntities;
        private readonly Action? _previousValidation;
        private readonly NestedSetTrackerSnapshot? _previousCallbackWrites;
        private readonly bool _previousPersistenceReached;
        private bool _disposed;

        /// <summary>Captures the outer context-local state before a nested plan replaces it.</summary>
        /// <param name="state">The integration state whose managed plan is about to be replaced.</param>
        /// <param name="tracker">The tracker whose detection setting is restored after this save.</param>
        internal ManagedSaveScope(
            SaveState state,
            ChangeTracker tracker
        )
        {
            _state = state;
            _tracker = tracker;
            _autoDetectChanges = tracker.AutoDetectChangesEnabled;
            _previousEntities = state.ManagedEntities;
            _previousValidation = state.ManagedValidation;
            _previousCallbackWrites = state.ManagedCallbackWrites;
            _previousPersistenceReached = state.ManagedPersistenceReached;
        }

        /// <summary>Gets ordinary callback writes captured at this plan's persistence step.</summary>
        /// <remarks>
        ///     Read this before disposal, even when the save throws: a SavedChanges callback can fail after EF accepted
        ///     the writes. Restoring the snapshot is also exact when EF itself failed and kept the pending state.
        /// </remarks>
        internal NestedSetTrackerSnapshot? CallbackWrites
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                return _state.ManagedCallbackWrites;
            }
        }

        /// <summary>Requires that EF passed this validated plan to its persistence step.</summary>
        /// <exception cref="NestedSetException">
        /// A save callback suppressed or emptied the planned insertion.
        /// </exception>
        internal void RequirePersisted()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!_state.ManagedPersistenceReached)
            {
                throw new NestedSetException(
                    NestedSetErrorCode.InvalidContext,
                    "Save callbacks must not suppress or empty a planned hierarchy insertion.");
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _tracker.AutoDetectChangesEnabled = _autoDetectChanges;
            _state.ManagedDepth--;
            _state.ManagedEntities = _previousEntities;
            _state.ManagedValidation = _previousValidation;
            _state.ManagedCallbackWrites = _previousCallbackWrites;
            _state.ManagedPersistenceReached = _previousPersistenceReached;
            _disposed = true;
        }
    }

    /// <summary>Restores the Parent-mutation bypass after one preplanned move batch.</summary>
    private sealed class ManagedMutation : IDisposable
    {
        private readonly SaveState _state;
        private bool _disposed;

        /// <summary>Retains the context-local state modified by the coordinated save.</summary>
        /// <param name="state">The state whose managed mutation depth was incremented.</param>
        internal ManagedMutation(
            SaveState state
        )
        {
            _state = state;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _state.ManagedMutationDepth--;
            _disposed = true;
        }
    }
}

namespace Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

/// <summary>Implements the context-local save coordinator behind the public extension methods.</summary>
internal static partial class NestedSetSaveChanges
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

            // WHY: This integration saves without acceptance so rollback can restore application writes.
            // SavedChanges callbacks therefore still see generated sidecars; validate before acceptance can
            // overwrite an unauthorized CLR key mutation with its provider-generated value.
            state.ManagedValidation?.Invoke();

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
}

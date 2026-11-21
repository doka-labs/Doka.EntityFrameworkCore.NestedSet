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
    /// <see cref="ExecuteAsync" />. Read-only hierarchy guards run before persistence without enabling detection.
    ///     Callbacks must not detach hierarchy entries, accept changes, or recursively save. Models without
    ///     hierarchy work retain the caller's native detection setting.
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
        var previousValidation = state.SaveValidation;

        void VerifyCandidates()
        {
            if (HasHierarchyChanges(context))
            {
                throw new NestedSetException(
                    NestedSetErrorCode.InvalidContext,
                    "Use nested-set facade operations to insert/delete nodes or change hierarchy-owned structure. "
                    + "Tracked Parent/order edits require the asynchronous SaveNestedSetChangesAsync wrapper.");
            }
        }

        // WHY: The existing persistence and zero-write completion guards cover all final pre-save state.
        // A DetectedAllChanges handler would scan it again without protecting another write boundary.
        state.Active = true;
        state.SaveValidation = VerifyCandidates;

        try
        {
            return saveChanges();
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = autoDetectChanges;
            state.SaveValidation = previousValidation;
            state.Active = false;
        }
    }

    /// <summary>Saves payload and uses a transaction or savepoint when configured sibling order needs repair.</summary>
    /// <param name="context">The configured caller-owned context.</param>
    /// <param name="acceptAllChangesOnSuccess">Whether to accept changes after the complete boundary succeeds.</param>
    /// <param name="saveChanges">
    ///     A delegate that calls base.SaveChangesAsync(false, token). It must not recurse into the override, accept
    ///     changes, mutate hierarchy entries, or detach entries. The coordinator invokes it
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
    ///     runs only when automatic detection is enabled. With detection disabled, Parent/order edits must be
    ///     explicitly marked or supplied through tracking notifications; unmarked hierarchy edits are rejected.
    ///     Read-only guards reject callback changes outside the authorized hierarchy plan without discovering
    ///     unrelated payload. The caller's detection setting is restored afterward.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///     Integration, tracking, or transaction requirements are violated.
    /// </exception>
    internal static async Task<int> ExecuteAsync(
        DbContext context,
        bool acceptAllChangesOnSuccess,
        Func<CancellationToken, Task<int>> saveChanges,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(saveChanges);
        cancellationToken.ThrowIfCancellationRequested();
        RequireConfigured(context);
        var state = s_contexts.GetValue(context, static _ => new SaveState());

        if (state.ManagedDepth > 0)
        {
            var managedCount = await saveChanges(cancellationToken)
                .ConfigureAwait(false);

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
            var count = await saveChanges(cancellationToken)
                .ConfigureAwait(false);

            if (acceptAllChangesOnSuccess)
            {
                context.ChangeTracker.AcceptAllChanges();
            }

            return count;
        }

        // WHY: Detection is an application choice even for hierarchy work. A global or local detection pass
        // would also discover unrelated payload; explicit flags and notifications already identify saved triggers.
        if (context.ChangeTracker.AutoDetectChangesEnabled)
        {
            context.ChangeTracker.DetectChanges();
        }

        if (HasHierarchyChanges(context, null, allowMarkedTriggers: true))
        {
            throw new NestedSetException(
                NestedSetErrorCode.InvalidContext,
                "Use facade operations for hierarchy-owned structure. When automatic detection is disabled, "
                + "explicitly mark Parent/order edits or use tracking notifications before saving.");
        }

        var candidates = FindCandidates(context);

        if (candidates.Count == 0)
        {
            state.Active = true;

            try
            {
                // WHY: A sorted entity elsewhere in the model must not change retries, ambient transactions,
                // isolation, or allocations for an unrelated save. The temporary guard runs before EF sends SQL.
                var ordinaryCount = await SaveGuardedAsync(context, saveChanges, null, null, cancellationToken)
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
        var guardPlan = CaptureGuardPlan(candidates);
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
                        async operationCancellationToken =>
                        {
                            var requests = new List<INestedSetTreeLockRequest>();

                            // WHY: Index this stable list directly without creating an enumerator or a copied buffer.
                            // ReSharper disable once ForCanBeConvertedToForeach
                            // ReSharper disable once LoopCanBeConvertedToQuery
                            for (int groupIndex = 0, groupCount = groups.Count; groupIndex < groupCount; groupIndex++)
                            {
                                var group = groups[groupIndex];

                                await group
                                    .ResolveScopesAsync(requests, operationCancellationToken)
                                    .ConfigureAwait(false);
                            }

                            await NestedSetSaveLocks
                                .AcquireAsync(context, requests, operationCancellationToken)
                                .ConfigureAwait(false);

                            // WHY: Index this stable list directly without creating an enumerator or a copied buffer.
                            // ReSharper disable once ForCanBeConvertedToForeach
                            // ReSharper disable once LoopCanBeConvertedToQuery
                            for (
                                int groupIndex2 = 0, groupCount2 = groups.Count;
                                groupIndex2 < groupCount2;
                                groupIndex2++
                            )
                            {
                                var group = groups[groupIndex2];

                                await group
                                    .PrepareLockedChangesAsync(operationCancellationToken)
                                    .ConfigureAwait(false);
                            }

                            // WHY: Index this stable list directly without creating an enumerator or a copied buffer.
                            // ReSharper disable once ForCanBeConvertedToForeach
                            // ReSharper disable once LoopCanBeConvertedToQuery
                            for (
                                int groupIndex3 = 0, groupCount3 = groups.Count;
                                groupIndex3 < groupCount3;
                                groupIndex3++
                            )
                            {
                                var group = groups[groupIndex3];

                                group.SuppressParentChanges();
                            }

                            try
                            {
                                foreach (var (entry, candidate) in guardPlan)
                                {
                                    candidate.CaptureSuppressedParent(entry);
                                }

                                savedCount = await SaveGuardedAsync(
                                        context,
                                        saveChanges,
                                        guardPlan,
                                        snapshot,
                                        operationCancellationToken)
                                    .ConfigureAwait(false);
                            }
                            finally
                            {
                                // WHY: This operation owns the stable indexed input; retain its established visit
                                // order.
                                // ReSharper disable once ForCanBeConvertedToForeach
                                // ReSharper disable once LoopCanBeConvertedToQuery
                                for (
                                    int groupIndex4 = 0, groupCount4 = groups.Count;
                                    groupIndex4 < groupCount4;
                                    groupIndex4++
                                )
                                {
                                    var group = groups[groupIndex4];

                                    group.RestoreParentChanges();
                                }
                            }

                            // WHY: Index this stable list directly without creating an enumerator or a copied buffer.
                            // ReSharper disable once ForCanBeConvertedToForeach
                            // ReSharper disable once LoopCanBeConvertedToQuery
                            for (
                                int groupIndex5 = 0, groupCount5 = groups.Count;
                                groupIndex5 < groupCount5;
                                groupIndex5++
                            )
                            {
                                var group = groups[groupIndex5];

                                await group
                                    .ApplyParentChangesAsync(operationCancellationToken)
                                    .ConfigureAwait(false);
                            }

                            // WHY: Index this stable list directly without creating an enumerator or a copied buffer.
                            // ReSharper disable once ForCanBeConvertedToForeach
                            // ReSharper disable once LoopCanBeConvertedToQuery
                            for (
                                int groupIndex6 = 0, groupCount6 = groups.Count;
                                groupIndex6 < groupCount6;
                                groupIndex6++
                            )
                            {
                                var group = groups[groupIndex6];

                                await group
                                    .ReorderAsync(operationCancellationToken)
                                    .ConfigureAwait(false);
                            }

                            // WHY: Index this stable list directly without creating an enumerator or a copied buffer.
                            // ReSharper disable once ForCanBeConvertedToForeach
                            // ReSharper disable once LoopCanBeConvertedToQuery
                            for (
                                int groupIndex7 = 0, groupCount7 = groups.Count;
                                groupIndex7 < groupCount7;
                                groupIndex7++
                            )
                            {
                                var group = groups[groupIndex7];

                                await group
                                    .RefreshAsync(operationCancellationToken)
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

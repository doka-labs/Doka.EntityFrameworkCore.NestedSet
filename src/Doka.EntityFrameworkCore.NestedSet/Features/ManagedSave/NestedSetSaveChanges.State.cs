namespace Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

internal static partial class NestedSetSaveChanges
{
    /// <summary>Tracks only context integration and bounded reentry; it never owns transaction resources.</summary>
    internal sealed class SaveState
    {
        /// <summary>Marks the one underlying save inside a guarded execution boundary.</summary>
        internal bool Active { get; set; }

        /// <summary>Gets or sets the read-only guard for the current ordinary or coordinated base save.</summary>
        internal Action? SaveValidation { get; set; }

        /// <summary>Captures written relationship values before SQL for completion after provider success.</summary>
        internal Func<IList<IUpdateEntry>, Action?>? SavePersistenceCompleted { get; set; }

        /// <summary>Gets or sets the detached-input proof and bounded scratch for the complete import.</summary>
        internal NestedSetImportInputs? ImportInputs { get; set; }

        /// <summary>Counts bounded internal insertion saves that already own their write protocol.</summary>
        internal int ManagedDepth { get; set; }

        /// <summary>Gets or sets the exact entity references allowed through the current insertion save.</summary>
        internal HashSet<object>? ManagedEntities { get; set; }

        /// <summary>Gets or sets the post-callback structural validation for the current insertion.</summary>
        internal Action? ManagedValidation { get; set; }

        /// <summary>Gets or sets bounded identity bookkeeping before EF accepts provider-generated values.</summary>
        internal Action? ManagedPersistenceCompleted { get; set; }

        /// <summary>Retains the exact validated native insertion set once EF reaches persistence.</summary>
        internal HashSet<IUpdateEntry>? ManagedPersistenceEntries { get; set; }

        /// <summary>Gets or sets pending application writes captured at the plan's persistence step.</summary>
        internal NestedSetTrackerSnapshot? ManagedCallbackWrites { get; set; }

        /// <summary>Counts preplanned Parent moves executed inside the coordinated save transaction.</summary>
        internal int ManagedMutationDepth { get; set; }
    }

    /// <summary>Owns one library-owned insertion save and restores the internal-save bypass even on failure.</summary>
    internal sealed class ManagedSaveScope : IDisposable
    {
        private readonly bool _autoDetectChanges;
        private readonly NestedSetTrackerSnapshot? _previousCallbackWrites;
        private readonly HashSet<object>? _previousEntities;
        private readonly Action? _previousPersistenceCompleted;
        private readonly HashSet<IUpdateEntry>? _previousPersistenceEntries;
        private readonly Action? _previousValidation;
        private readonly SaveState _state;
        private readonly ChangeTracker _tracker;
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
            _previousPersistenceCompleted = state.ManagedPersistenceCompleted;
            _previousCallbackWrites = state.ManagedCallbackWrites;
            _previousPersistenceEntries = state.ManagedPersistenceEntries;
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

        /// <summary>Gets the exact native insertion set already checked at the persistence boundary.</summary>
        /// <exception cref="ObjectDisposedException">This managed-save scope is already disposed.</exception>
        /// <exception cref="NestedSetException">The planned insertion never reached persistence.</exception>
        internal IReadOnlySet<IUpdateEntry> PersistedEntries
        {
            get
            {
                RequirePersisted();

                return _state.ManagedPersistenceEntries!;
            }
        }

        /// <summary>Gets the matching input reference set without copying the validated save plan.</summary>
        /// <exception cref="ObjectDisposedException">This managed-save scope is already disposed.</exception>
        /// <exception cref="NestedSetException">The planned insertion never reached persistence.</exception>
        internal IReadOnlySet<object> PersistedEntities
        {
            get
            {
                RequirePersisted();

                return _state.ManagedEntities!;
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
            _state.ManagedPersistenceCompleted = _previousPersistenceCompleted;
            _state.ManagedCallbackWrites = _previousCallbackWrites;
            _state.ManagedPersistenceEntries = _previousPersistenceEntries;
            _disposed = true;
        }

        /// <summary>Requires that EF passed this validated plan to its persistence step.</summary>
        /// <exception cref="NestedSetException">
        /// A save callback suppressed or emptied the planned insertion.
        /// </exception>
        internal void RequirePersisted()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_state.ManagedPersistenceEntries is null)
            {
                throw new NestedSetException(
                    NestedSetErrorCode.InvalidContext,
                    "Save callbacks must not suppress or empty a planned hierarchy insertion.");
            }
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

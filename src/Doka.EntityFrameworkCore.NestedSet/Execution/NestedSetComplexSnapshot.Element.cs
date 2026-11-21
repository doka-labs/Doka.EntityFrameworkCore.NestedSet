namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal sealed partial class NestedSetComplexSnapshot
{
    /// <summary>Retains registered elements, including original-only deletions and current additions.</summary>
    private sealed class ElementSnapshot
    {
        private readonly bool _hasCurrent;
        private readonly EntityState _state;
        private int _borrowedSlot = -1;
        private bool _heldAsDeleted;

        /// <summary>Reads native identity and positions without requiring a current CLR element.</summary>
        internal ElementSnapshot(
            object native,
            int currentSlot,
            bool hasCurrent
        )
        {
            Native = native;
            CurrentSlot = currentSlot;
            _hasCurrent = hasCurrent;
            CapturedOrdinal = NestedSetComplexEntryAccess.Ordinal(native, false);
            CapturedOriginalOrdinal = NestedSetComplexEntryAccess.Ordinal(native, true);
            _state = NestedSetComplexEntryAccess.State(native);
        }

        /// <summary>The same native entry used by all previously captured scalar wrappers.</summary>
        internal object Native { get; }

        /// <summary>The captured current ordinal, including EF's absent-lane sentinel.</summary>
        internal int CapturedOrdinal { get; }

        /// <summary>The captured original ordinal, including EF's absent-lane sentinel.</summary>
        internal int CapturedOriginalOrdinal { get; }

        /// <summary>The captured current lane position, independent of a Deleted ordinal sentinel.</summary>
        internal int CurrentSlot { get; }

        /// <summary>The original lane position, which can coexist with EF's minus-one Added sentinel.</summary>
        internal int OriginalSlot { get; set; } = -1;

        /// <summary>Whether this Deleted element also occupies a current slot.</summary>
        internal bool RetainsDeletedSlot => _state == EntityState.Deleted && CurrentSlot >= 0;

        /// <summary>Addresses a Deleted or Detached element whose ordinal sentinel has no list position.</summary>
        /// <param name="slot">A free staged slot; EF does not validate ordinals in these two states.</param>
        internal void Borrow(
            int slot
        )
        {
            _borrowedSlot = slot;
            NestedSetComplexEntryAccess.SetOrdinal(Native, slot, false);
        }

        /// <summary>Keeps a dual-lane element's staged slot, as EF clears its ordinal on becoming Deleted.</summary>
        /// <param name="slot">The captured staged slot that its descendants continue to address.</param>
        internal void KeepSlotAcrossDeletion(
            int slot
        )
        {
            if (_state == EntityState.Deleted || _heldAsDeleted)
            {
                _borrowedSlot = slot;
            }
        }

        /// <summary>Requests a temporary Deleted state while this owner's children reinstall.</summary>
        internal void HoldAsDeleted() => _heldAsDeleted = _state is EntityState.Unchanged or EntityState.Modified;

        /// <summary>Returns a held owner to its captured position and state after its children settled.</summary>
        /// <returns>Whether this element was held.</returns>
        internal bool ReleaseHold()
        {
            if (!_heldAsDeleted)
            {
                return false;
            }

            _heldAsDeleted = false;
            _borrowedSlot = -1;
            NestedSetComplexEntryAccess.SetOrdinal(Native, CapturedOrdinal, false);
            NestedSetComplexEntryAccess.SetState(Native, _state);

            return true;
        }

        /// <summary>Restores a state that EF overwrote while a containing owner returned to life.</summary>
        internal void SettleState() => NestedSetComplexEntryAccess.SetState(Native, _state);

        /// <summary>Drops any live-graph address and returns to the captured positions before reinstallation.</summary>
        internal void EndRetirement()
        {
            _borrowedSlot = -1;
            RestoreOrdinals();
        }

        /// <summary>Reapplies the borrowed slot after EF's Deleted transition reset the current ordinal.</summary>
        internal void BorrowAgain()
        {
            if (_borrowedSlot >= 0)
            {
                NestedSetComplexEntryAccess.SetOrdinal(Native, _borrowedSlot, false);
            }
        }

        /// <summary>Returns the captured ordinal after all descendants no longer need this temporary path.</summary>
        internal void Release()
        {
            if (_borrowedSlot >= 0)
            {
                _borrowedSlot = -1;
                NestedSetComplexEntryAccess.SetOrdinal(Native, CapturedOrdinal, false);
            }
        }

        /// <summary>Allows scalar flags to settle Modified/Unchanged without graph rebinding.</summary>
        internal bool MatchesLanes(
            IList current,
            IList original
        )
        {
            var state = NestedSetComplexEntryAccess.State(Native);
            var compatibleState = state == _state
                || (_hasCurrent
                    && state is EntityState.Modified or EntityState.Unchanged
                    && _state is EntityState.Modified or EntityState.Unchanged);

            return compatibleState
                && NestedSetComplexEntryAccess.Ordinal(Native, false) == CapturedOrdinal
                && NestedSetComplexEntryAccess.Ordinal(Native, true) == CapturedOriginalOrdinal
                && (CurrentSlot < 0
                    || (CurrentSlot < current.Count && ReferenceEquals(current[CurrentSlot], Native)))
                && (OriginalSlot < 0
                    || (OriginalSlot < original.Count && ReferenceEquals(original[OriginalSlot], Native)));
        }

        /// <summary>Restores positions while detached, then uses EF's normal registration notifications.</summary>
        internal void Restore()
        {
            RestoreOrdinals();

            if (HasBothLanes())
            {
                // WHY: EF retains both slots only when a tracked element transitions beneath an already
                // Added/Deleted parent. Direct Detached-to-Added/Deleted registration installs only one lane.
                NestedSetComplexEntryAccess.SetState(Native, EntityState.Unchanged);
            }

            NestedSetComplexEntryAccess.SetState(
                Native,
                _heldAsDeleted
                    ? EntityState.Deleted
                    : _state);
        }

        /// <summary>Provides captured access paths while the same native identity remains detached.</summary>
        private void RestoreOrdinals()
        {
            var dualLane = HasBothLanes();
            NestedSetComplexEntryAccess.SetOrdinal(
                Native,
                dualLane
                    ? CurrentSlot
                    : CapturedOrdinal,
                false);

            NestedSetComplexEntryAccess.SetOrdinal(
                Native,
                dualLane
                    ? OriginalSlot
                    : CapturedOriginalOrdinal,
                true);
        }

        /// <summary>Recognizes the opposite-lane pointers retained beneath Added and Deleted containers.</summary>
        private bool HasBothLanes() => (_state == EntityState.Deleted && CurrentSlot >= 0)
            || (_state == EntityState.Added && OriginalSlot >= 0);

        /// <summary>Checks lifecycle and both positional identities after all scalar flag changes.</summary>
        internal void Verify(
            object owner,
            IComplexProperty property
        )
        {
            var current = NestedSetComplexEntryAccess.Entries(owner, property, false);
            var original = NestedSetComplexEntryAccess.Entries(owner, property, true);

            if (NestedSetComplexEntryAccess.State(Native) != _state
                || NestedSetComplexEntryAccess.Ordinal(Native, false) != CapturedOrdinal
                || NestedSetComplexEntryAccess.Ordinal(Native, true) != CapturedOriginalOrdinal
                || (CurrentSlot >= 0
                    && (CurrentSlot >= current.Count || !ReferenceEquals(current[CurrentSlot], Native)))
                || (OriginalSlot >= 0
                    && (OriginalSlot >= original.Count || !ReferenceEquals(original[OriginalSlot], Native))))
            {
                throw new InvalidOperationException(
                    "The coordinated save could not restore a complex element state: "
                    + $"{property.Name}; state={NestedSetComplexEntryAccess.State(Native)}/{_state}; "
                    + $"ordinal={NestedSetComplexEntryAccess.Ordinal(Native, false)}/{CapturedOrdinal}; "
                    + $"originalOrdinal={NestedSetComplexEntryAccess.Ordinal(Native, true)}/{CapturedOriginalOrdinal}; "
                    + $"current={CurrentSlot}/{current.Count}; original={OriginalSlot}/{original.Count}.");
            }
        }
    }
}

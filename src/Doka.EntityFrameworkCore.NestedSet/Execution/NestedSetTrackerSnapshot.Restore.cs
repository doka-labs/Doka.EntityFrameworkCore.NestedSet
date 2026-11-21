namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal sealed partial class NestedSetTrackerSnapshot
{
    /// <summary>Restores accepted state, complex topology and scalar state in cross-entry phases.</summary>
    private void RestoreEntryState()
    {
        foreach (var entry in _entries)
        {
            entry.RestoreAcceptedState();
        }

        foreach (var entry in _entries)
        {
            entry.RestoreComplexTopology();
        }

        // WHY: Restoring principal keys can update dependents; values must settle before flags are restored.
        foreach (var entry in _entries)
        {
            entry.RestoreValues();
        }

        foreach (var entry in _entries)
        {
            entry.RestoreState();
        }

        foreach (var entry in _entries)
        {
            entry.RestoreProperties();
        }
    }

    /// <summary>Replays modification, CLR and conceptual-null intent after native relationship fixup.</summary>
    private void RestoreEntryIntent()
    {
        foreach (var entry in _entries)
        {
            entry.RestoreModificationFlags();
            entry.RestoreComplexFlags();
        }

        // WHY: Temporary keys may live only in EF sidecars while the caller's CLR property still contains zero.
        foreach (var entry in _entries)
        {
            entry.RestoreClrValues();
        }

        // WHY: Deferred orphan deletion clears conceptual nulls, and earlier scalar/state writes can clear them.
        foreach (var entry in _entries)
        {
            entry.RestoreConceptualNulls();
        }
    }

    /// <summary>Restores final membership snapshots and retires inverse receipts only after verification.</summary>
    private void RestoreAuthoritativeRelationships()
    {
        foreach (var entry in _entries)
        {
            entry.RestoreIndexerMembership();
        }

        // WHY: Authoritative pre-save snapshots win over inverse deltas, including originally present members
        // that a callback removed before the library re-added them. Late callback graph copies are never used.
        UndoPersistedAdditions();
        _relationships?.RestoreSnapshots();
        _relationships?.Verify();

        foreach (var entry in _entries)
        {
            entry.Verify();
        }

        // WHY: A successful recovery can restore an originally present member after undoing its receipt.
        // Retire only verified receipts so a repeated Restore cannot remove that original membership again.
        _persistedAdditions = null;
    }
}

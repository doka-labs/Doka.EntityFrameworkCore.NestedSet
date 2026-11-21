namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal sealed partial class NestedSetComplexSnapshot
{
    private sealed partial class CollectionSnapshot
    {
        /// <summary>Reinstalls registered identities through EF's own state and positional callbacks.</summary>
        internal void Restore(
            HashSet<object> captured
        )
        {
            // WHY: Restoring a containing element can initialize new nested entries. Remove these local
            // registrations before reinstalling the captured children; never replace private native lists.
            DetachCollection(Owner, _property, new HashSet<object>(ReferenceEqualityComparer.Instance), captured);
            DetachRetained(captured);

            // WHY: Deleted/Added parents can retain opposite-lane pointers after detachment. EF's own
            // capacity method clears both lanes before rebuilding them, including their captured null holes.
            NestedSetComplexEntryAccess.Capacity(Owner, _property, 0, 0);
            NestedSetComplexEntryAccess.RestoreOriginal(Owner, _property, _original);

            // WHY: A wrapper-created element can keep an original ordinal without any original lane slot, while
            // EF validates that ordinal on every transition. RestoreCapacity trims this temporary length again.
            var current = _currentCount;
            var original = _originalCount;

            foreach (var element in Elements)
            {
                current = Math.Max(current, element.CapturedOrdinal + 1);
                original = Math.Max(original, element.CapturedOriginalOrdinal + 1);
            }

            NestedSetComplexEntryAccess.Capacity(Owner, _property, current, original);

            foreach (var element in Elements)
            {
                HideNestedOriginals(element.Native);
                element.Restore();
                element.BorrowAgain();
            }
        }

        /// <summary>Hides this frame's original so a containing transition creates no original-lane entries.</summary>
        internal void HideOriginal() => NestedSetComplexEntryAccess.RestoreOriginal(Owner, _property, null);

        /// <summary>Returns elements held as Deleted to their captured state.</summary>
        /// <returns>Whether any element was held.</returns>
        internal bool ReleaseHeldOwners()
        {
            var released = false;

            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var element in Elements)
            {
                released |= element.ReleaseHold();
            }

            return released;
        }

        /// <summary>Repairs elements whose state a returning owner cascaded over.</summary>
        internal void SettleStates()
        {
            foreach (var element in Elements)
            {
                element.SettleState();
            }
        }

        /// <summary>Returns borrowed ordinals once every nested transition has settled.</summary>
        internal void ReleaseAddresses()
        {
            foreach (var element in Elements)
            {
                element.Release();
            }
        }

        /// <summary>Prevents EF from creating original-lane children during a containing element transition.</summary>
        private void HideNestedOriginals(
            object native
        )
        {
            // WHY: EF fills empty original slots with new Deleted entries that have no original buffer. Their
            // own nested collection reads then recurse between ReadPropertyValue and GetOriginalValue until the
            // stack overflows. Each nested frame reinstalls its captured original and native children itself.
            foreach (var nested in _property.ComplexType.GetFlattenedComplexProperties())
            {
                if (nested.IsCollection)
                {
                    NestedSetComplexEntryAccess.RestoreOriginal(native, nested, null);
                }
            }
        }

        /// <summary>Retires captured identities even when callbacks removed them from both cached lanes.</summary>
        internal void DetachRetained(
            HashSet<object> captured
        )
        {
            var current = Math.Max(
                _currentCount,
                NestedSetComplexEntryAccess.Entries(Owner, _property, false)
                    .Count);

            var original = Math.Max(
                _originalCount,
                NestedSetComplexEntryAccess.Entries(Owner, _property, true)
                    .Count);

            foreach (var element in Elements)
            {
                current = Math.Max(current, NestedSetComplexEntryAccess.Ordinal(element.Native, false) + 1);
                original = Math.Max(original, NestedSetComplexEntryAccess.Ordinal(element.Native, true) + 1);
            }

            NestedSetComplexEntryAccess.Capacity(Owner, _property, current, original);

            foreach (var element in Elements)
            {
                // WHY: Detection or acceptance can remove an identity from both lanes while leaving it
                // tracked. Its real Detached transition must precede ordinal changes and parent reactivation.
                DetachCollections(
                    element.Native,
                    _property.ComplexType,
                    new HashSet<object>(ReferenceEqualityComparer.Instance),
                    captured);

                NestedSetComplexEntryAccess.SetState(element.Native, EntityState.Detached);
            }
        }
    }
}

namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal sealed partial class NestedSetComplexSnapshot
{
    private sealed partial class CollectionSnapshot
    {

        /// <summary>Builds an access graph for the ordinals EF will walk during the coming transitions.</summary>
        /// <param name="root">The containing entity whose CLR graph supplies the staged paths.</param>
        /// <param name="retiring">Whether callback entries still exist, so that live references need access.</param>
        internal void Stage(
            object root,
            bool retiring
        )
        {
            var path = LivePath();

            if (path.Any(ordinal => ordinal < 0))
            {
                return;
            }

            var staging = _staging = new StagingSnapshot { Path = path };
            RestoreReferences(root, _originalReferences);

            if (retiring)
            {
                RestoreReferences(root, _liveReferences);
            }

            var depth = 0;
            var parent = ReadContaining(_property.DeclaringType, root, path, ref depth);

            if (parent is null)
            {
                // WHY: EF's getter ends at a missing container before any collection ordinal is indexed.
                return;
            }

            staging.Previous = _property.GetGetter().GetClrValue(parent);
            var previous = staging.Previous as IList;
            var count = StagingCount(previous);
            var addressed = Elements
                .Where(element => !retiring
                    || NestedSetComplexEntryAccess.State(element.Native) != EntityState.Detached)
                .ToArray();

            count = PrepareElementSlots(addressed, count, retiring);

            if (count == 0)
            {
                return;
            }

            var staged = CreateStagingCollection(addressed, count);

            if (previous is not null && HasSameItems(previous, staged))
            {
                // WHY: A copying mapped setter would replace otherwise valid caller-owned storage.
                return;
            }

            Install(root, staged, staging);
        }

        /// <summary>Resolves a retained element's CLR instance from its current or original position.</summary>
        private object? Instance(
            ElementSnapshot element
        ) => CurrentItem(element.CurrentSlot)
            ?? (_original is IList originals && (uint)element.OriginalSlot < (uint)originals.Count
                ? originals[element.OriginalSlot]
                : null);

        /// <summary>Installs temporary access storage without exposing it to caller collection code.</summary>
        private void Install(
            object root,
            IList staged,
            StagingSnapshot staging
        )
        {
            // WHY: A field-mapped setter is EF's direct write, so caller collections stay untouched: temporary
            // null slots raise no events and reach no validating Add. Only a property-mode setter can run code
            // that copies the assigned list; growing its instance is the sole identity-preserving alternative.
            if (_property.GetMemberInfo(forMaterialization: false, forSet: true) is PropertyInfo
                && staging.Previous is IList { IsReadOnly: false, IsFixedSize: false } mutable)
            {
                staging.PreviousItems = new object?[mutable.Count];
                mutable.CopyTo(staging.PreviousItems, 0);
                mutable.Clear();

                foreach (var item in staged)
                {
                    mutable.Add(item);
                }

                return;
            }

            staging.Installed = true;
            NestedSetComplexEntryAccess
                .Setter(_property)
                .SetClrValueUsingContainingEntity(root, staging.Path, staged);
        }

        /// <summary>Checks access-graph identity without invoking caller equality or mapped setters.</summary>
        private static bool HasSameItems(
            IList current,
            IList staged
        )
        {
            if (current.Count != staged.Count)
            {
                return false;
            }

            // WHY: Complex elements have reference identity. Indexed IList access avoids enumerator boxing
            // while proving that skipping the setter preserves every required native ordinal.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < current.Count; index++)
            {
                if (!ReferenceEquals(current[index], staged[index]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Removes temporary containers while their containing current paths remain reachable.</summary>
        internal void UndoStage(
            object root
        )
        {
            var staging = _staging;

            if (staging is null)
            {
                return;
            }

            if (staging.Installed)
            {
                NestedSetComplexEntryAccess
                    .Setter(_property)
                    .SetClrValueUsingContainingEntity(root, staging.Path, staging.Previous);
            }

            if (staging.PreviousItems is { } items)
            {
                var previous = (IList)staging.Previous!;
                previous.Clear();

                foreach (var item in items)
                {
                    previous.Add(item);
                }
            }

            if (staging.References is null)
            {
                _staging = null;

                return;
            }

            // WHY: A nested optional reference must be removed before its temporary parent is hidden.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = staging.References.Count - 1; index >= 0; index--)
            {
                NestedSetComplexEntryAccess
                    .Setter(staging.References[index])
                    .SetClrValueUsingContainingEntity(root, staging.Path, null);
            }

            _staging = null;
        }

        /// <summary>Provides existing reference containers for native cleanup.</summary>
        private void RestoreReferences(
            object root,
            List<(IComplexProperty Property, object Instance)>? references
        )
        {
            if (references is null)
            {
                return;
            }

            var path = _staging!.Path;

            // WHY: Reference replay only reads this owned, stable reference receipt list.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var referenceIndex = 0; referenceIndex < references.Count; referenceIndex++)
            {
                var (property, instance) = references[referenceIndex];

                var depth = 0;
                var parent = ReadContaining(property.DeclaringType, root, path, ref depth);

                if (parent is not null
                    && property
                        .GetGetter()
                        .GetClrValue(parent) is null)
                {
                    // WHY: A raw-null or replaced optional reference can hide native nested entries.
                    // Existing original/live containers are temporary access, never the final CLR preimage.
                    NestedSetComplexEntryAccess
                        .Setter(property)
                        .SetClrValueUsingContainingEntity(root, path, instance);

                    (_staging.References ??= []).Add(property);
                }
            }
        }

        /// <summary>Allocates temporary access receipts only on the native-rebind failure path.</summary>
        private sealed class StagingSnapshot
        {
            /// <summary>The ordinal path this phase addressed, which later collection changes must not move.</summary>
            internal required int[] Path { get; init; }

            /// <summary>The CLR collection reference replaced by the temporary access list.</summary>
            internal object? Previous { get; set; }

            /// <summary>Whether the temporary collection setter needs to be reversed.</summary>
            internal bool Installed { get; set; }

            /// <summary>Original membership when temporary access grows the existing mapped list in place.</summary>
            internal object?[]? PreviousItems { get; set; }

            /// <summary>Optional references temporarily initialized above the staged collection.</summary>
            internal List<IComplexProperty>? References { get; set; }
        }
    }
}

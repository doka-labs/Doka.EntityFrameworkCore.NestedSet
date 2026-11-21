namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal static partial class NestedSetInsertionTracking
{
    internal sealed partial class Identity
    {
        /// <summary>Captures both current and intermediate installed key slots before any CLR replay.</summary>
        private void CaptureAdditionalIdentities(
            ref List<Exception>? errors
        )
        {
            // WHY: SavingChanges or a later Tracking callback can trigger EF detection and install a new
            // scalar identity before the structural guard rejects it. Capture only this entry's exact current
            // map membership before restoring CLR values, so both staged and subsequently installed slots unwind.
            // Indexing keeps each key aligned with its installed identity snapshot.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var keyIndex = 0; keyIndex < _keys.Length; keyIndex++)
            {
                var key = _keys[keyIndex];

                try
                {
                    CaptureAdditionalKey(key, _values[keyIndex], relationship: false);

                    // WHY: EF can install an intermediate key through detection, then a callback can edit CLR
                    // again before throwing. Its relationship snapshot identifies that exact installed slot;
                    // the original staged and final CLR values alone cannot find it.
                    CaptureAdditionalKey(key, _values[keyIndex], relationship: true);
                }
                catch (Exception error)
                {
                    (errors ??= []).Add(error);
                }
            }
        }

        /// <summary>Restores captured key and CLR values before ordinary EF detachment performs fixup.</summary>
        private void RestoreInstalledValues(
            ref List<Exception>? errors
        )
        {
            // WHY: Each key ordinal selects the same metadata, readers and installed value vectors.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var keyIndex = 0; keyIndex < _keys.Length; keyIndex++)
            {
                var properties = _keys[keyIndex].Properties;
                var shape = s_shapes.GetValue(_keys[keyIndex], CreateShape);

                // WHY: Property ordinals keep typed readers and both restoration snapshots aligned.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var propertyIndex = 0; propertyIndex < properties.Count; propertyIndex++)
                {
                    var property = properties[propertyIndex];
                    var reader = shape.Readers[propertyIndex];

                    try
                    {
                        if (property.IsShadowProperty())
                        {
                            var expected = _values[keyIndex][propertyIndex];

                            if (!reader.MatchesCurrent(UpdateEntry, expected))
                            {
                                // WHY: A shadow owned FK can alias its principal's mutable key. Native fixup
                                // must see the installed identity before detachment, without flagging a new
                                // application key edit or introducing an artificial entity-state transition.
                                Contract.SetProperty(
                                    UpdateEntry,
                                    property,
                                    NestedSetStructuralValue.Snapshot(property, expected));
                            }
                        }
                        else
                        {
                            var expected = _entry.State == EntityState.Added
                                ? _clrValues[keyIndex][propertyIndex]
                                : _values[keyIndex][propertyIndex];

                            if (!reader.MatchesClr(_entry.Entity, expected))
                            {
                                NestedSetStructuralValue.Assign(
                                    _entry.Entity,
                                    property,
                                    NestedSetStructuralValue.Snapshot(property, expected));
                            }
                        }
                    }
                    catch (Exception error)
                    {
                        (errors ??= []).Add(error);
                    }
                }
            }
        }
    }
}

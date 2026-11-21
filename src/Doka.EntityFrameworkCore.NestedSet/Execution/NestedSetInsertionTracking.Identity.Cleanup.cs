namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal static partial class NestedSetInsertionTracking
{
    internal sealed partial class Identity
    {
        /// <summary>Removes installed primary identities while retaining independent cleanup errors.</summary>
        private void RemovePrimaryIdentities(
            ref List<Exception>? errors
        )
        {
            // WHY: Remove each identity using the installed snapshot at the same key ordinal.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < _keys.Length; index++)
            {
                try
                {
                    var map = Contract.FindMap(_manager, _keys[index]);

                    if (map is not null)
                    {
                        Contract
                            .Map(map.GetType())
                            .Remove(map, _values[index], UpdateEntry);
                    }
                }
                catch (Exception error)
                {
                    (errors ??= []).Add(error);
                }
            }
        }

        /// <summary>Removes this entry from its captured buckets without scanning unrelated dependents.</summary>
        private void RemoveDependentMemberships(
            ref List<Exception>? errors
        )
        {
            if (_buckets is { } buckets)
            {
                // WHY: Only native bucket membership changes here; the captured bucket list remains stable.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var bucketIndex = 0; bucketIndex < buckets.Count; bucketIndex++)
                {
                    var bucket = buckets[bucketIndex];

                    try
                    {
                        // WHY: A composite FK can share the principal's mutable CLR object. EF's ordinary
                        // key-based removal cannot find a changed hash bucket; its captured exact collection
                        // can remove this dependent directly without scanning or clearing unrelated entries.
                        bucket.Remove(UpdateEntry);
                    }
                    catch (Exception error)
                    {
                        (errors ??= []).Add(error);
                    }
                }
            }
        }

        /// <summary>Removes callback-installed extra keys through their exact retained native identities.</summary>
        private void RemoveAdditionalIdentities(
            ref List<Exception>? errors
        )
        {
            if (_additionalKeys is { } additionalKeys)
            {
                // WHY: Remove only these captured extra keys while their receipt list remains stable.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var additionalKeyIndex = 0; additionalKeyIndex < additionalKeys.Count; additionalKeyIndex++)
                {
                    var (key, values) = additionalKeys[additionalKeyIndex];

                    try
                    {
                        var map = Contract.FindMap(_manager, key);

                        if (map is not null)
                        {
                            Contract
                                .Map(map.GetType())
                                .Remove(map, values, UpdateEntry);
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

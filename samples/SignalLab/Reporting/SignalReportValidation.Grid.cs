namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

internal static partial class SignalReportValidation
{
    /// <summary>Indexes selected scalar sums and preserves every foreign identity for duplicate rejection.</summary>
    private struct GridAccumulator(
        ValidatedSignalReport request,
        int capacity
    )
    {
        // WHY: Two optional ordinal buffers use at most 512 stack bytes, independent of selection size.
        private const int StackOrdinalLimit = 64;

        private readonly Dictionary<int, double> _values = new(capacity);
        private readonly bool _canIndex = CanIndexSelectedCells(request);
        private Dictionary<Guid, int>? _anchors;
        private Dictionary<Guid, int>? _channels;
        private Dictionary<CapturePort, int>? _sources;
        private HashSet<(Guid AnchorId, Guid ChannelId, CapturePort Source)>? _foreignKeys;
        private (Guid AnchorId, Guid ChannelId, CapturePort Source)? _duplicate;

        /// <summary>Checks complete tuple identity without retaining materialized aggregate DTOs.</summary>
        internal bool Add(SignalReportTotal total)
        {
            var anchor = _canIndex ? FindIndex(request.AnchorIds, ref _anchors, total.AnchorId) : -1;
            var channel = anchor >= 0 ? FindIndex(request.ChannelIds, ref _channels, total.ChannelId) : -1;
            var source = channel >= 0 ? FindIndex(request.Sources, ref _sources, total.Source) : -1;
            var selected = anchor >= 0 && channel >= 0 && source >= 0;
            var added = selected
                ? AddSelected(Offset(anchor, channel, source), total.Value)
                : (_foreignKeys ??= []).Add((total.AnchorId, total.ChannelId, total.Source));

            if (!added)
            {
                // WHY: The async reader must reach EOF before rejecting its first duplicate. Unselected
                // identities participate too; ignoring their values must not hide malformed SQL groups.
                _duplicate ??= (total.AnchorId, total.ChannelId, total.Source);
            }

            return added;
        }

        /// <summary>Preserves first-duplicate identity for either immediate or deferred rejection.</summary>
        internal void RequireUnique()
        {
            if (_duplicate is { } duplicate)
            {
                // WHY: Let the framework preserve its localized message, tuple formatting and parameter
                // contract. This one-entry dictionary is allocated only on a rejected report's error path.
                var duplicates = new Dictionary<(Guid AnchorId, Guid ChannelId, CapturePort Source), byte>(1)
                {
                    [duplicate] = 0,
                };

                duplicates.Add(duplicate, 0);
            }
        }

        /// <summary>Rejects duplicate groups before checked sizing and creates only the final selected grid.</summary>
        internal SignalReportCell[] Complete()
        {
            RequireUnique();

            // WHY: The offset index never reserves the Cartesian grain or throws on an unrepresentable one.
            // Only this final check may reject its size, after complete reading and duplicate validation.
            var count = checked(request.AnchorIds.Length * request.ChannelIds.Length * request.Sources.Length);
            var result = new SignalReportCell[count];

            if (_values.Count == 0)
            {
                // WHY: Foreign totals still completed duplicate validation and checked sizing above.
                // Missing-only grids need no membership maps, ordinal buffers, or per-cell dictionary probes.
                var missing = 0;

                foreach (var anchor in request.AnchorIds)
                {
                    foreach (var channel in request.ChannelIds)
                    {
                        foreach (var source in request.Sources)
                        {
                            result[missing++] = new SignalReportCell(anchor, channel, source, false, 0);
                        }
                    }
                }

                return result;
            }

            var repeatAnchors = HasRepeatedSelections(request.AnchorIds, _anchors);
            var repeatChannels = HasRepeatedSelections(request.ChannelIds, _channels);
            var repeatSources = HasRepeatedSelections(request.Sources, _sources);

            // WHY: Normal snapshotted selections need no ordinal buffers. Repeated internal-adapter choices
            // are resolved once; bounded stack storage avoids small arrays without risking a large stack frame.
            var channels = repeatChannels
                ? request.ChannelIds.Length <= StackOrdinalLimit
                    ? stackalloc int[request.ChannelIds.Length] : new int[request.ChannelIds.Length]
                : [];

            var sources = repeatSources
                ? request.Sources.Length <= StackOrdinalLimit
                    ? stackalloc int[request.Sources.Length] : new int[request.Sources.Length]
                : [];

            FillRepeatedOrdinals(request.ChannelIds, _channels, channels);
            FillRepeatedOrdinals(request.Sources, _sources, sources);
            var index = 0;

            // WHY: Preserve requested anchor order and resolve each repeated anchor ordinal once per output row group.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var anchor = 0; anchor < request.AnchorIds.Length; anchor++)
            {
                // WHY: This outer loop resolves each anchor position only once. Its repeated first ordinal
                // needs no buffer; larger selections already have the membership map populated by Add.
                var anchorIndex = !repeatAnchors ? anchor : _anchors is null
                    ? Array.IndexOf(request.AnchorIds, request.AnchorIds[anchor], 0, anchor + 1)
                    : _anchors[request.AnchorIds[anchor]];

                // WHY: Reuse precomputed channel ordinals while preserving requested output order.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var channel = 0; channel < request.ChannelIds.Length; channel++)
                {
                    var channelIndex = channels.IsEmpty ? channel : channels[channel];
                    var prefix = (anchorIndex * request.ChannelIds.Length + channelIndex) * request.Sources.Length;

                    // WHY: Fill the flat output grid using the matching anchor, channel and source ordinals.
                    // ReSharper disable once ForCanBeConvertedToForeach
                    // ReSharper disable once LoopCanBeConvertedToQuery
                    for (var source = 0; source < request.Sources.Length; source++)
                    {
                        var sourceIndex = sources.IsEmpty ? source : sources[source];
                        var exists = _values.TryGetValue(prefix + sourceIndex, out var value);
                        result[index++] = new SignalReportCell(request.AnchorIds[anchor], request.ChannelIds[channel],
                            request.Sources[source], exists, value);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Caps only a near-full terminal resize while keeping sparse growth proportional to observed rows.
        /// </summary>
        private bool AddSelected(
            int offset,
            double value
        )
        {
            if (_values.Count > 0 && _values.Count == _values.Capacity)
            {
                // WHY: A stored total passed _canIndex and every axis membership. The complete grain is
                // therefore positive and fits Int32; deriving it only at a full boundary cannot move overflow
                // rejection ahead of EOF. Reading actual BCL capacity avoids duplicated capacity-tracking state.
                var maximum = request.AnchorIds.Length * request.ChannelIds.Length * request.Sources.Length;
                if (maximum > _values.Count && maximum < (long)_values.Count * 2 && !_values.ContainsKey(offset))
                {
                    // WHY: Only a new identity at a near-full terminal boundary justifies reserving the grain.
                    // An otherwise sparse report still grows solely from observed rows; duplicates do not resize.
                    _values.EnsureCapacity(maximum);
                }
            }

            return _values.TryAdd(offset, value);
        }

        /// <summary>Maps one selected identity to the final layout only when that layout fits an integer.</summary>
        private readonly int Offset(
            int anchor,
            int channel,
            int source
        ) => (((anchor * request.ChannelIds.Length) + channel) * request.Sources.Length) + source;

        /// <summary>Checks offset representability without moving overflow rejection ahead of reader EOF.</summary>
        private static bool CanIndexSelectedCells(ValidatedSignalReport selection)
        {
            // WHY: The first product fits in Int64 for every possible array length. Division bounds the
            // second product without throwing; empty or oversized layouts keep only full duplicate identities.
            // Complete still evaluates the original checked products, including prefix overflow with no sources.
            var cardMetrics = (long)selection.AnchorIds.Length * selection.ChannelIds.Length;

            return cardMetrics is > 0 and <= int.MaxValue && selection.Sources.Length > 0
                && cardMetrics <= int.MaxValue / selection.Sources.Length;
        }

        /// <summary>Uses first-occurrence ordinals, with lazy constant-time lookup for larger selections.</summary>
        private static int FindIndex<T>(
            T[] selection,
            ref Dictionary<T, int>? indexes,
            T value
        ) where T : notnull
        {
            // WHY: The ordinary four-anchor/two-SignalLab/three-source sample needs no lookup-map allocations.
            // Capping linear work at eight keeps larger selections at expected O(rows + selection) cost;
            // maps are allocated only after an actual total arrives and never reserve the sparse result grain.
            if (selection.Length <= 8)
            {
                return Array.IndexOf(selection, value);
            }

            if (indexes is null)
            {
                indexes = new Dictionary<T, int>(selection.Length);
                // WHY: Store the first position of repeated selections for exact grid lookup.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var index = 0; index < selection.Length; index++)
                {
                    // WHY: The synchronous adapter also accepts repeated selection entries. Their repeated
                    // output positions must refer to the same total rather than reject or split its identity.
                    indexes.TryAdd(selection[index], index);
                }
            }

            return indexes.TryGetValue(value, out var found) ? found : -1;
        }

        /// <summary>
        /// Detects actual repeated choices without allocating membership state for small unique selections.
        /// </summary>
        private static bool HasRepeatedSelections<T>(
            T[] selection,
            Dictionary<T, int>? indexes
        ) where T : notnull
        {
            if (indexes is not null)
            {
                return indexes.Count < selection.Length;
            }

            // WHY: The first selected total initializes every axis map larger than eight. With stored
            // values, this null-map scan therefore remains bounded to eight choices.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 1; index < selection.Length; index++)
            {
                if (Array.IndexOf(selection, selection[index], 0, index) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Precomputes first positions only for an axis whose repeated output positions require them.
        /// </summary>
        private static void FillRepeatedOrdinals<T>(
            T[] selection,
            Dictionary<T, int>? indexes,
            Span<int> ordinals
        ) where T : notnull
        {
            // WHY: Fill the caller-owned ordinal span with the first matching selection positions.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < ordinals.Length; index++)
            {
                ordinals[index] = indexes is null
                    ? Array.IndexOf(selection, selection[index], 0, index + 1)
                    : indexes[selection[index]];
            }
        }
    }
}

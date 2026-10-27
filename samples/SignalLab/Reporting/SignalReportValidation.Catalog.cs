namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

internal static partial class SignalReportValidation
{
    /// <summary>Retains only exact scalar catalog evidence until the complete reader has been disposed.</summary>
    private struct CatalogAccumulator()
    {
        private readonly HashSet<Guid> _anchors = [];
        private readonly HashSet<Guid> _channels = [];
        private readonly HashSet<(Guid TypeId, string FieldCode)> _definitions = [];
        private readonly HashSet<(Guid TypeId, string FieldCode, string Value)> _choices = [];
        private string? _unit;
        private bool _invalidUnit;

        /// <summary>Collects evidence without rejecting catalog rows before successful EOF and disposal.</summary>
        internal void Add(
            SignalCatalogRow row
        )
        {
            // WHY: Tuple string equality is ordinal. Database collation must not relax authorization;
            // every SignalLab row contributes to the unit check even when its identity is duplicated or unselected.
            switch (row.Kind)
            {
                case 0:
                    _anchors.Add(row.Id);
                    break;
                case 1:
                    _channels.Add(row.Id);
                    _invalidUnit |= string.IsNullOrEmpty(row.UnitCode)
                        || (_unit is not null && !StringComparer.Ordinal.Equals(_unit, row.UnitCode));
                    _unit ??= row.UnitCode;
                    break;
                case 2:
                    _definitions.Add((row.TypeId, row.FieldCode));
                    break;
                case 3:
                    _choices.Add((row.TypeId, row.FieldCode, row.Value));
                    break;
            }
        }

        /// <summary>Applies authorization, unit and metadata rejection precedence after complete consumption.</summary>
        internal void Complete(
            ValidatedSignalReport request
        )
        {
            if (!_anchors.IsSupersetOf(request.AnchorIds)
                || !_channels.IsSupersetOf(request.ChannelIds))
            {
                throw new ArgumentException(
                    "A selected anchor or channel is missing, hidden, outside this tree, or unauthorized.",
                    nameof(request));
            }

            if (_invalidUnit || _unit is null)
            {
                throw new ArgumentException(
                    "One additive report requires a common explicitly declared unit.",
                    nameof(request));
            }

            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var group in request.Groups)
            {
                // ReSharper disable once LoopCanBeConvertedToQuery
                foreach (var criterion in group.Criteria)
                {
                    if (!_definitions.Contains((group.TypeId, criterion.FieldCode))
                        || !_choices.Contains((group.TypeId, criterion.FieldCode, criterion.Value)))
                    {
                        throw new ArgumentException(
                            "Unknown field, owner classification, or fixed case-sensitive choice.",
                            nameof(request));
                    }
                }
            }
        }
    }
}

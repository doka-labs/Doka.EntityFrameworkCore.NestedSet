namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Validates fixed sample contracts before constructing or executing a report.</summary>
internal static partial class SignalReportValidation
{
    /// <summary>Snapshots selections and rejects malformed requests without any database access.</summary>
    internal static ValidatedSignalReport Snapshot(
        SignalReportRequest request
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.AnchorIds);
        ArgumentNullException.ThrowIfNull(request.ChannelIds);
        ArgumentNullException.ThrowIfNull(request.Sources);
        ArgumentNullException.ThrowIfNull(request.Criteria);

        if (request.TenantId == Guid.Empty
            || request.TreeId == Guid.Empty
            || request.FromInclusive >= request.UntilExclusive
            || !Enum.IsDefined(request.Granularity))
        {
            throw new ArgumentException(
                "A report requires an authorized tenant/tree, a nonempty window and a known grain.",
                nameof(request));
        }

        var anchors = request
            .AnchorIds
            .Distinct()
            .ToArray();

        var channels = request
            .ChannelIds
            .Distinct()
            .ToArray();

        var sources = request
            .Sources
            .Distinct()
            .ToArray();

        if (anchors.Length == 0
            || channels.Length == 0
            || sources.Length == 0
            || anchors.Contains(Guid.Empty)
            || channels.Contains(Guid.Empty)
            || sources.Any(source => !Enum.IsDefined(source)))
        {
            throw new ArgumentException(
                "Select at least one valid anchor, channel and acquisition port.",
                nameof(request));
        }

        var groups = SnapshotGroups(request);

        return new ValidatedSignalReport(
            request.TenantId,
            request.TreeId,
            anchors,
            channels,
            sources,
            request.FromInclusive,
            request.UntilExclusive,
            request.Granularity,
            groups);
    }

    /// <summary>Validates all requested anchors, channels and scalar choices against one batched catalog result.</summary>
    internal static void RequireCatalog(
        ValidatedSignalReport request,
        IReadOnlyCollection<SignalCatalogRow> catalog
    )
    {
        var accumulator = new CatalogAccumulator();
        foreach (var row in catalog)
        {
            accumulator.Add(row);
        }

        accumulator.Complete(request);
    }

    /// <summary>Checks inclusive direct-edge validity without adding a day to the maximum supported date.</summary>
    internal static bool IsAllowed(
        DateOnly from,
        DateOnly? to,
        DateOnly measurement
    ) => from <= measurement && (to is null || measurement <= to.Value);

    /// <summary>Rejects invalid intervals before saving, independently of the relational check constraint.</summary>
    internal static void RequirePeriod(
        DateOnly from,
        DateOnly? to
    )
    {
        if (to < from)
        {
            throw new ArgumentException("The inclusive end day cannot precede the start day.", nameof(to));
        }
    }

    /// <summary>
    /// Validates a normal sample write while the database constraint independently protects bypasses.
    /// </summary>
    internal static void AddPeriod(
        SignalLabContext context,
        LinkWindow period
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(period);
        RequirePeriod(period.FromDate, period.ToDate);
        context.LinkAvailability.Add(period);
    }

    /// <summary>
    /// Fills only the selected small result grid while preserving the distinction between no data and zero.
    /// </summary>
    internal static SignalReportCell[] CreateGrid(
        ValidatedSignalReport request,
        IReadOnlyCollection<SignalReportTotal> totals
    )
    {
        var accumulator = new GridAccumulator(request, totals.Count);
        foreach (var total in totals)
        {
            if (!accumulator.Add(total))
            {
                // WHY: Unlike an async database reader, this already materialized collection historically
                // rejects its first duplicate immediately, before any later enumerator failure.
                accumulator.RequireUnique();
            }
        }

        return accumulator.Complete();
    }

    /// <summary>Snapshots owner groups in first-use order without rebuilding grouping sets per scalar field.</summary>
    private static TagGroup[] SnapshotGroups(
        SignalReportRequest request
    )
    {
        var criteria = request.Criteria;
        if (criteria.Count == 0)
        {
            return [];
        }

        if (criteria.Any(criterion => criterion is null))
        {
            throw new ArgumentException("A metadata criterion cannot be null.", nameof(request));
        }

        var byType = new Dictionary<Guid, TagGroupBuilder>();
        var orderedGroups = new List<TagGroupBuilder>();
        foreach (var criterion in criteria)
        {
            if (criterion.TypeId == Guid.Empty
                || !Enum.IsDefined(criterion.OwnerMode)
                || criterion.Operator != TagOperator.Equal
                || string.IsNullOrEmpty(criterion.FieldCode)
                || criterion.Value is null)
            {
                throw new ArgumentException(
                    "A metadata group requires one known owner mode and supported scalar fields.",
                    nameof(request));
            }

            if (!byType.TryGetValue(criterion.TypeId, out var group))
            {
                group = new TagGroupBuilder(criterion.TypeId, criterion.OwnerMode);
                byType.Add(criterion.TypeId, group);
                orderedGroups.Add(group);
            }

            // WHY: The scalar accumulator is an implementation detail; its errors still identify the original
            // request argument rather than exposing an internal criterion or hardcoding an unrelated parameter.
            group.Add(criterion, nameof(request));
        }

        // WHY: Explicit lists preserve first-selection order independently of dictionary enumeration order.
        // Scalar equality and a common mode make one entry per exact field sufficient for deduplication.
        var groups = new TagGroup[orderedGroups.Count];
        // WHY: Fill the final group array in first-selection order without an intermediate projection.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < orderedGroups.Count; index++)
        {
            groups[index] = orderedGroups[index].Build();
        }

        return groups;
    }

    /// <summary>Accumulates one classification's exact scalar fields in their first-requested order.</summary>
    private sealed class TagGroupBuilder(
        Guid typeId,
        TagOwnerMode mode
    )
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
        private readonly List<TagCriterion> _criteria = [];

        /// <summary>Rejects inconsistent modes and scalar contradictions while dropping identical criteria.</summary>
        /// <param name="criterion">The scalar field selected by the original request.</param>
        /// <param name="parameterName">The outer request parameter name preserved by this validation boundary.</param>
        internal void Add(
            TagCriterion criterion,
            string parameterName
        )
        {
            if (criterion.OwnerMode != mode)
            {
                throw new ArgumentException(
                    "A metadata group requires one known owner mode and supported scalar fields.",
                    parameterName);
            }

            if (_values.TryGetValue(criterion.FieldCode, out var value))
            {
                if (!StringComparer.Ordinal.Equals(value, criterion.Value))
                {
                    throw new ArgumentException(
                        "Different values of the same scalar field contradict an AND request.",
                        parameterName);
                }

                return;
            }

            _values.Add(criterion.FieldCode, criterion.Value);
            _criteria.Add(criterion);
        }

        /// <summary>Freezes a private array so later caller selection changes cannot alter this report.</summary>
        internal TagGroup Build() => new(typeId, mode, _criteria.ToArray());
    }
}

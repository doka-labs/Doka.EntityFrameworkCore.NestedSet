using System.Linq.Expressions;
using System.Reflection;

namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Builds one scalar report while leaving topology, owner selection and direct paths to NestedSet.</summary>
internal static partial class SignalReportBuilder
{
    // WHY: These fixed projections contain no context or request. Reusing their immutable expressions avoids
    // recreating property metadata and member bindings for every report without retaining application state.
    private static readonly Expression<Func<PulseRecord, MeasurementRow>> s_pulseMeasurement = value => new()
    {
        TenantId = value.TenantId,
        NodeId = value.NodeId,
        ChannelId = value.ChannelId,
        Granularity = value.Granularity,
        Day = value.Day,
        Value = value.PulseCount,
        Source = CapturePort.Pulse,
    };

    private static readonly Expression<Func<SweepRecord, MeasurementRow>> s_sweepMeasurement = value => new()
    {
        TenantId = value.TenantId,
        NodeId = value.ReceiverId,
        ChannelId = value.StreamId,
        Granularity = value.Resolution,
        Day = value.CompletedOn,
        Value = value.IntegratedTicks,
        Source = CapturePort.Sweep,
    };

    private static readonly Expression<Func<BurstRecord, MeasurementRow>> s_burstMeasurement = value => new()
    {
        TenantId = value.TenantId,
        NodeId = value.DeviceId,
        ChannelId = value.SignalId,
        Granularity = value.BucketSize,
        Day = value.CapturedOn,
        Value = value.Magnitude,
        Source = CapturePort.Burst,
    };

    private static readonly MethodInfo s_metadataAny = typeof(Queryable)
        .GetMethods()
        .Single(method => method.Name == nameof(Queryable.Any)
            && method.GetParameters()
                .Length
            == 2)
        .MakeGenericMethod(typeof(TagValue));

    private static readonly MethodInfo s_periodAny = s_metadataAny
        .GetGenericMethodDefinition()
        .MakeGenericMethod(typeof(LinkWindow));

    private static readonly Expression<Func<ReportPath, SignalNode, SignalNode, LinkWindow, bool>> s_allowedPeriod =
        (row, parent, child, period) => period.ParentId == parent.Id
            && period.ChildId == child.Id
            && period.FromDate <= row.Day
            && (period.ToDate == null || row.Day <= period.ToDate.Value);

    private static readonly MemberExpression s_periodTenant = Expression.Property(
        s_allowedPeriod.Parameters[3],
        nameof(LinkWindow.TenantId));

    private static readonly MethodInfo s_textConcat =
        ((MethodCallExpression)((Expression<Func<string, string, string>>)((
            left,
            right
        ) => string.Concat(left, right))).Body).Method;

    /// <summary>Moves otherwise ignored trailing spaces inside the exact text comparison.</summary>
    private const string TextComparisonSuffix = "|";

    private static readonly ConstantExpression s_comparisonSuffix = Expression.Constant(TextComparisonSuffix);

    // WHY: These immutable stored-column nodes contain no context, model or request. Each predicate binds
    // the shared parameters in its own lambda; criterion constants and tenant captures stay per construction.
    private static readonly ParameterExpression s_owner = Expression.Parameter(typeof(SignalNode), "owner");
    private static readonly MemberExpression s_ownerId = Expression.Property(s_owner, nameof(SignalNode.Id));
    private static readonly MemberExpression s_ownerType = Expression.Property(s_owner, nameof(SignalNode.TypeId));
    private static readonly ParameterExpression s_reportPath = Expression.Parameter(typeof(ReportPath), "row");
    private static readonly ParameterExpression s_metadataValue = Expression.Parameter(typeof(TagValue), "value");

    private static readonly MemberExpression s_metadataTenant = Expression.Property(
        s_metadataValue,
        nameof(TagValue.TenantId));

    private static readonly MemberExpression s_metadataNode = Expression.Property(
        s_metadataValue,
        nameof(TagValue.NodeId));

    private static readonly MemberExpression s_metadataType = Expression.Property(
        s_metadataValue,
        nameof(TagValue.TypeId));

    private static readonly MemberExpression s_fieldCode = Expression.Property(
        s_metadataValue,
        nameof(TagValue.FieldCode));

    private static readonly MemberExpression s_valueText = Expression.Property(s_metadataValue, nameof(TagValue.Value));

    // WHY: The fixed scalar operators avoid rediscovering their metadata for every equality in every criterion.
    private static readonly MethodInfo? s_textEquality = Expression.Equal(s_fieldCode, s_fieldCode)
        .Method;

    private static readonly MethodInfo? s_guidEquality = Expression.Equal(s_ownerId, s_ownerId)
        .Method;

    private static readonly BinaryExpression s_fieldCodeWithSuffix = Expression.Add(
        s_fieldCode,
        s_comparisonSuffix,
        s_textConcat);

    private static readonly BinaryExpression s_valueTextWithSuffix = Expression.Add(
        s_valueText,
        s_comparisonSuffix,
        s_textConcat);

    /// <summary>Executes catalog validation and one aggregate reader within the same qualified read snapshot.</summary>
    internal static Task<SignalReportCell[]> ReadAsync(
        SignalLabContext context,
        SignalReportRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var validated = SignalReportValidation.Snapshot(request);

        return SampleReadSnapshot.RunAsync(
            context,
            async token =>
            {
                // WHY: Each reader feeds compact scalar state and reaches EOF/disposal before validation.
                // Keeping DTO histories would add two intermediate buffers without changing the final array contract.
                await SignalReportValidation.RequireCatalogAsync(
                    validated,
                    Catalog(context, validated)
                        .AsAsyncEnumerable(),
                    token);
                // WHY: A buffered provider can finish the catalog after cancellation. Reject the next
                // stage before building or submitting an aggregate that the caller no longer needs.
                token.ThrowIfCancellationRequested();

                return await SignalReportValidation.CreateGridAsync(
                    validated,
                    Build(context, validated)
                        .AsAsyncEnumerable(),
                    token);
            },
            cancellationToken);
    }
}

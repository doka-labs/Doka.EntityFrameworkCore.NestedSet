using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

internal static partial class SignalReportBuilder
{
    /// <summary>Binds the actual period root and tenant to fixed direct-edge and inclusive-day expressions.</summary>
    private static Expression<Func<ReportPath, SignalNode, SignalNode, bool>> AllowedPeriods(
        SignalLabContext context,
        Guid tenant
    )
    {
        var scope = RequestedGuid(tenant);
        var body = Expression.AndAlso(
            Expression.Equal(s_periodTenant, scope, false, s_guidEquality),
            s_allowedPeriod.Body);

        var allowed = Expression.Lambda<Func<LinkWindow, bool>>(body, s_allowedPeriod.Parameters[3]);
        var periods = ((IQueryable<LinkWindow>)context.LinkAvailability).Expression;
        var exists = Expression.Call(s_periodAny, periods, Expression.Quote(allowed));

        return Expression.Lambda<Func<ReportPath, SignalNode, SignalNode, bool>>(
            exists,
            s_allowedPeriod.Parameters[0],
            s_allowedPeriod.Parameters[1],
            s_allowedPeriod.Parameters[2]);
    }

    /// <summary>Keeps every criterion of a classification on one owner, with captured scalar parameters.</summary>
    /// <remarks>
    /// The equivalent benchmark reference shares only this domain predicate, not hierarchy or path composition.
    /// </remarks>
    internal static Expression<Func<Contribution, SignalNode, bool>> OwnerPredicate(
        SignalLabContext context,
        Guid tenant,
        TagGroup group
    ) => OwnerPredicate<Contribution>(context, tenant, group);

    /// <summary>Binds a nearest classification without rediscovering its member and equality operator.</summary>
    /// <param name="type">The classification snapshot owned by this metadata group.</param>
    /// <returns>An owner-only candidate whose private scalar slot remains parameterized by EF.</returns>
    private static Expression<Func<ReportPath, SignalNode, bool>> NearestOwnerCandidate(
        Guid type
    )
    {
        // WHY: Each group retains its own scalar capture, but the immutable member/operator metadata
        // is identical. Candidate selection still precedes all value and complete-path decisions.
        var candidate = Expression.Equal(s_ownerType, RequestedGuid(type), false, s_guidEquality);

        return Expression.Lambda<Func<ReportPath, SignalNode, bool>>(candidate, s_reportPath, s_owner);
    }

    /// <summary>Constructs the same owner-only decision for either report paths or reference contributions.</summary>
    private static Expression<Func<TRow, SignalNode, bool>> OwnerPredicate<TRow>(
        SignalLabContext context,
        Guid tenant,
        TagGroup group
    )
    {
        var row = Expression.Parameter(typeof(TRow), "row");
        var owner = s_owner;
        var value = s_metadataValue;
        var ownerId = s_ownerId;
        var ownerType = s_ownerType;
        var tenantValue = RequestedGuid(tenant);

        // WHY: Nearest selects the classification in candidate before checking values. Keeping that
        // selection out of its value predicate makes no-fallback semantics explicit and avoids duplicate work.
        Expression? predicate = group.Mode == TagOwnerMode.Any
            ? Expression.Equal(ownerType, RequestedGuid(group.TypeId), false, s_guidEquality)
            : null;

        var matchesOwner = Expression.AndAlso(
            Expression.Equal(s_metadataTenant, tenantValue, false, s_guidEquality),
            Expression.AndAlso(
                Expression.Equal(s_metadataNode, ownerId, false, s_guidEquality),
                Expression.Equal(s_metadataType, ownerType, false, s_guidEquality)));

        var metadata = ((IQueryable<TagValue>)context.TagValues).Expression;

        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var criterion in group.Criteria)
        {
            // WHY: Reuse the same immutable owner expression rather than rebuild and then visit a lambda
            // for every field. Request-local fields retain distinct parameter slots without property getters.
            var captured = Expression.Constant(new OwnerCriterionParameters(criterion.FieldCode, criterion.Value));
            var fields = Expression.AndAlso(
                ExactText(
                    s_fieldCode,
                    s_fieldCodeWithSuffix,
                    Expression.Field(captured, s_requestedOwnerFieldCode),
                    Expression.Field(captured, s_requestedOwnerFieldCodeWithSuffix)),
                ExactText(
                    s_valueText,
                    s_valueTextWithSuffix,
                    Expression.Field(captured, s_requestedOwnerValue),
                    Expression.Field(captured, s_requestedOwnerValueWithSuffix)));

            var allowed = Expression.Lambda<Func<TagValue, bool>>(Expression.AndAlso(matchesOwner, fields), value);

            var exists = Expression.Call(s_metadataAny, metadata, Expression.Quote(allowed));
            predicate = predicate is null ? exists : Expression.AndAlso(predicate, exists);
        }

        return Expression.Lambda<Func<TRow, SignalNode, bool>>(predicate ?? Expression.Constant(true), row, owner);
    }

    /// <summary>Preserves ordinary indexed equality while making stored trailing spaces significant.</summary>
    private static BinaryExpression ExactText(
        MemberExpression stored,
        BinaryExpression storedWithSuffix,
        MemberExpression requested,
        MemberExpression requestedWithSuffix
    )
    {
        // WHY: MySQL PAD SPACE and SQL Server comparisons ignore trailing spaces even with binary collations.
        // A shared nonspace suffix moves those spaces inside the comparison without trimming either value.

        return Expression.AndAlso(
            Expression.Equal(stored, requested, false, s_textEquality),
            Expression.Equal(storedWithSuffix, requestedWithSuffix, false, s_textEquality));
    }
}

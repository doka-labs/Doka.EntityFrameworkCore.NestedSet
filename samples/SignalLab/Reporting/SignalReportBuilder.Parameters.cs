using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

internal static partial class SignalReportBuilder
{
    // WHY: Instance fields are parameterized by EF without invoking reflected request-property getters.
    // Each holder belongs to one construction; equal text retains independent slots and a stable query shape.
    private static readonly FieldInfo s_requestedGuid =
        (FieldInfo)((MemberExpression)((Expression<Func<StrongBox<Guid>, Guid>>)(slot => slot.Value)).Body).Member;

    private static readonly FieldInfo s_requestedFieldCode =
        (FieldInfo)((MemberExpression)
            ((Expression<Func<CriterionParameters, string>>)(criterion => criterion.FieldCode)).Body).Member;

    private static readonly FieldInfo s_requestedValue =
        (FieldInfo)((MemberExpression)((Expression<Func<CriterionParameters, string>>)(criterion => criterion.Value))
            .Body).Member;

    private static readonly FieldInfo s_requestedOwnerFieldCode =
        (FieldInfo)((MemberExpression)((Expression<Func<OwnerCriterionParameters, string>>)(criterion =>
            criterion.FieldCode)).Body).Member;

    private static readonly FieldInfo s_requestedOwnerValue =
        (FieldInfo)((MemberExpression)
            ((Expression<Func<OwnerCriterionParameters, string>>)(criterion => criterion.Value)).Body).Member;

    private static readonly FieldInfo s_requestedOwnerFieldCodeWithSuffix =
        (FieldInfo)((MemberExpression)((Expression<Func<OwnerCriterionParameters, string>>)(criterion =>
            criterion.FieldCodeWithSuffix)).Body).Member;

    private static readonly FieldInfo s_requestedOwnerValueWithSuffix =
        (FieldInfo)((MemberExpression)((Expression<Func<OwnerCriterionParameters, string>>)(criterion =>
            criterion.ValueWithSuffix)).Body).Member;

    /// <summary>
    /// Binds one request-local GUID slot without constructing a discarded lambda or capturing a request.
    /// </summary>
    private static MemberExpression RequestedGuid(
        Guid value
    ) => Expression.Field(Expression.Constant(new StrongBox<Guid>(value)), s_requestedGuid);

    /// <summary>Owns one criterion's immutable scalar slots during deferred catalog binding.</summary>
    /// <param name="fieldCode">The exact requested field code.</param>
    /// <param name="value">The exact requested scalar value.</param>
    private sealed class CriterionParameters(
        string fieldCode,
        string value
    )
    {
        /// <summary>Stores a distinct field-code slot even when another slot contains equal text.</summary>
        internal readonly string FieldCode = fieldCode;

        /// <summary>Stores the value slot without a reflected property getter or a second holder object.</summary>
        internal readonly string Value = value;
    }

    /// <summary>Owns one owner's ordinary and suffixed scalar parameter slots for one query construction.</summary>
    private sealed class OwnerCriterionParameters
    {
        /// <summary>Stores the original field code for ordinary indexed equality.</summary>
        internal readonly string FieldCode;

        /// <summary>Stores the original value for ordinary indexed equality.</summary>
        internal readonly string Value;

        /// <summary>Stores the field code with its exact comparison suffix already appended.</summary>
        internal readonly string FieldCodeWithSuffix;

        /// <summary>Stores the value with its exact comparison suffix already appended.</summary>
        internal readonly string ValueWithSuffix;

        /// <summary>Captures immutable text and prepares the two owner-only comparison strings once.</summary>
        /// <param name="fieldCode">The original field code, including any significant characters.</param>
        /// <param name="value">The original scalar value, including empty text or significant padding.</param>
        internal OwnerCriterionParameters(
            string fieldCode,
            string value
        )
        {
            FieldCode = fieldCode;
            Value = value;

            // WHY: These strings depend only on immutable request text. Binding prepared fields prevents EF
            // from interpreting request-side concatenation on each read; stored-column concatenation stays SQL.
            FieldCodeWithSuffix = string.Concat(fieldCode, TextComparisonSuffix);
            ValueWithSuffix = string.Concat(value, TextComparisonSuffix);
        }
    }
}

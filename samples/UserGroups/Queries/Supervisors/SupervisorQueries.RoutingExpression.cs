using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>
/// Reuses immutable routing shapes while binding request values, field arrays, and deferred owner sources.
/// </summary>
internal static partial class SupervisorQueries
{
    private static readonly RoutingTemplate[] s_routingTemplates = CreateRoutingTemplates();

    /// <summary>Applies routing to the already selected structural group without reselecting its owner.</summary>
    private static IQueryable<SupervisorCandidatePath> ApplyRouting(
        UserGroupContext context,
        Guid tenantId,
        SupervisorRequest request,
        IQueryable<SupervisorCandidatePath> pairs
    )
    {
        var row = Expression.Parameter(typeof(SupervisorCandidatePath), "row");
        var routing = RoutingPredicate(context, tenantId, request, row);

        if (routing is null)
        {
            return pairs;
        }

        // WHY: One scalar CASE protects the complete same-candidate AND from top-level semijoin flattening.
        // Sharing this wrapper also avoids a new Lambda, Where, and fence for every additional scalar match.
        var fenced = Expression.NotEqual(
            Expression.Condition(routing, Expression.Constant(1L), Expression.Constant(0L)),
            Expression.Constant(0L));

        return pairs.Where(Expression.Lambda<Func<SupervisorCandidatePath, bool>>(fenced, row));
    }

    /// <summary>Binds exact owner conditions to one shared candidate alias without compiling delegates.</summary>
    private static Expression? RoutingPredicate(
        UserGroupContext context,
        Guid tenantId,
        SupervisorRequest request,
        ParameterExpression row
    )
    {
        if (request.Matches.Count == 0)
        {
            return null;
        }

        var sources = new IQueryable<SupervisorRoutingValue>?[3];
        Expression? condition = null;

        // WHY: One rowset per exact owner pair keeps the metadata query independent of criterion count.
        // Canonical owner order also keeps equivalent requests on one EF shape when callers reorder owner pairs.
        var ownerPairs = request
            .Matches
            .GroupBy(match => (match.SourceOwner, match.CandidateOwner))
            .OrderBy(matches => matches.Key.SourceOwner)
            .ThenBy(matches => matches.Key.CandidateOwner);

        foreach (var matches in ownerPairs)
        {
            var source = SharedRoutingValues(context, tenantId, matches.Key.SourceOwner, sources);
            var target = SharedRoutingValues(context, tenantId, matches.Key.CandidateOwner, sources);
            var sourceIndex = matches.Key.SourceOwner switch
            {
                MetadataOwner.SourceUser => 0,
                MetadataOwner.SourceMembership => 1,
                _ => 2,
            };

            var targetIndex = matches.Key.CandidateOwner switch
            {
                MetadataOwner.CandidateUser => 0,
                MetadataOwner.CandidateMembership => 1,
                _ => 2,
            };

            // WHY: Both arrays come from the same nonempty group, so every generated index has both fields.
            // Raw strings remain ordinary provider parameters; no delimiter encoding or client metadata reads occur.
            var sourceFields = matches
                .Select(match => match.SourceFieldCode)
                .ToArray();

            var targetFields = matches
                .Select(match => match.CandidateFieldCode)
                .ToArray();

            var expression = s_routingTemplates[(sourceIndex * 3) + targetIndex]
                .Bind(
                    row,
                    request.RoutingUserId ?? request.SourceUserId,
                    request.RoutingGroupId,
                    sourceFields,
                    targetFields,
                    source,
                    target);

            condition = condition is null ? expression : Expression.AndAlso(condition, expression);
        }

        return condition;
    }

    /// <summary>
    /// Creates the nine supported owner-pair rowset shapes once rather than copying their metadata query per field.
    /// </summary>
    private static RoutingTemplate[] CreateRoutingTemplates()
    {
        var templates = new RoutingTemplate[9];
        for (var source = 0; source < 3; source++)
        {
            for (var target = 0; target < 3; target++)
            {
                templates[source * 3 + target] = new RoutingTemplate(
                    source != 2,
                    source != 0,
                    target != 2,
                    target != 0);
            }
        }

        return templates;
    }

    /// <summary>Stores one reusable server expression for an explicit source/candidate owner pair.</summary>
    private sealed class RoutingTemplate
    {
        private readonly Expression<Func<SupervisorCandidatePath, RoutingArguments, IQueryable<SupervisorRoutingValue>,
            IQueryable<SupervisorRoutingValue>, bool>> _expression;

        /// <summary>
        /// Captures fixed owner semantics; no request, context, or query is retained by the static cache.
        /// </summary>
        internal RoutingTemplate(
            bool sourceUser,
            bool sourceGroup,
            bool targetUser,
            bool targetGroup
        )
        {
            // WHY: Keep each target probe scalar inside its matching source field. A flattened
            // value-equality semijoin can otherwise pair every equal-valued field with every other
            // field before applying their criterion indices (4 x 4 rather than four exact pairs).
            Expression<Func<SupervisorCandidatePath, RoutingArguments, IQueryable<SupervisorRoutingValue>,
                IQueryable<SupervisorRoutingValue>, bool>> expression = (
                row,
                arguments,
                source,
                target
            ) => EF
                .Parameter(arguments.Indices)
                .All(index => source.Any(left =>
                    // WHY: The text inliner replaces this marker with string comparisons before EF translation.
                    // ReSharper disable once EntityFramework.UnsupportedServerSideFunctionCall
                    SupervisorExactText.FieldEquals(left.FieldCode, arguments.SourceFields.ElementAt(index))
                    && left.UserId == (sourceUser ? arguments.UserId : 0)
                    && left.GroupId == (sourceGroup ? arguments.GroupId ?? row.Candidate.StartGroupId : 0)
                    // WHY: The text inliner replaces this marker with string comparisons before EF translation.
                    // ReSharper disable once EntityFramework.UnsupportedServerSideFunctionCall
                    && (target.Any(right =>
                        SupervisorExactText.FieldEquals(right.FieldCode, arguments.TargetFields.ElementAt(index))
                        && right.UserId == (targetUser ? row.Candidate.CandidateUserId : 0)
                        && right.GroupId == (targetGroup ? row.Candidate.FoundGroupId : 0)
                        // WHY: The text inliner expands this marker to null, empty and equality checks.
                        // ReSharper disable once EntityFramework.UnsupportedServerSideFunctionCall
                        && SupervisorExactText.ValueEquals(left.Value, right.Value))
                        ? 1L
                        : 0L)
                    != 0L));

            _expression =
                (Expression<Func<SupervisorCandidatePath, RoutingArguments, IQueryable<SupervisorRoutingValue>,
                    IQueryable<SupervisorRoutingValue>, bool>>)SupervisorExactText.Inline(expression);
        }

        /// <summary>
        /// Inlines only immutable request values and ordinary query roots without invocation or compilation.
        /// </summary>
        /// <param name="user">The current or explicitly retained initial routing user.</param>
        /// <param name="row">The shared mapped candidate-path alias for every match in this request.</param>
        /// <param name="group">The retained initial routing group, or null for the current origin.</param>
        /// <param name="sourceFields">The nonempty source catalog fields in criterion order.</param>
        /// <param name="targetFields">The aligned candidate catalog fields in the same criterion order.</param>
        /// <param name="source">The deferred, tenant-bound source-owner values.</param>
        /// <param name="target">The deferred, tenant-bound candidate-owner values.</param>
        /// <returns>
        /// A provider expression whose request values use scalar or primitive-collection SQL parameters.
        /// </returns>
        internal Expression Bind(
            ParameterExpression row,
            int user,
            int? group,
            string[] sourceFields,
            string[] targetFields,
            IQueryable<SupervisorRoutingValue> source,
            IQueryable<SupervisorRoutingValue> target
        )
        {
            // WHY: Reusing the complete shape avoids reflection and rebuilding each extra scalar criterion.
            Expression[] values =
            [
                Expression.Constant(
                    new RoutingArguments(
                        user,
                        group,
                        sourceFields,
                        targetFields,
                        Enumerable
                            .Range(0, sourceFields.Length)
                            .ToArray())),
                source.Expression,
                target.Expression,
            ];

            var visitor = new RoutingBindingVisitor(_expression.Parameters, values, row);

            return visitor.Visit(_expression.Body);
        }
    }

    /// <summary>Owns captured request values and aligned arrays for EF's ordinary parameter extraction.</summary>
    /// <param name="UserId">The explicit routing user.</param>
    /// <param name="GroupId">The retained routing group, when applicable.</param>
    /// <param name="SourceFields">The source catalog fields, aligned with the candidate fields.</param>
    /// <param name="TargetFields">The candidate catalog fields, aligned with the source fields.</param>
    /// <param name="Indices">Every valid shared index of the two internally constructed arrays.</param>
    private sealed record RoutingArguments(
        int UserId,
        int? GroupId,
        string[] SourceFields,
        string[] TargetFields,
        int[] Indices
    );

    /// <summary>Substitutes a finite known parameter set and leaves unchanged expression subtrees shared.</summary>
    private sealed class RoutingBindingVisitor(
        IReadOnlyList<ParameterExpression> parameters,
        Expression[] values,
        ParameterExpression row
    ) : ExpressionVisitor
    {
        /// <inheritdoc />
        protected override Expression VisitParameter(
            ParameterExpression node
        )
        {
            if (ReferenceEquals(node, parameters[0]))
            {
                return row;
            }

            // WHY: Match parameters by reference and read the replacement at the corresponding offset.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 1; index < parameters.Count; index++)
            {
                if (ReferenceEquals(node, parameters[index]))
                {
                    return values[index - 1];
                }
            }

            return node;
        }
    }
}

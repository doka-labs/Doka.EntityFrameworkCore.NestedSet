namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

/// <summary>Completes deferred strategy selection after EF has expanded captures and before query translation.</summary>
internal sealed class NestedSetQueryExpressionInterceptor : IQueryExpressionInterceptor
{
    /// <inheritdoc />
    public Expression QueryCompilationStarting(
        Expression queryExpression,
        QueryExpressionEventData eventData
    )
    {
        // WHY: EF extracts captured queries before choosing its compiled-query cache key. Rewriting here
        // follows the exact source chosen for this execution, rather than freezing an earlier getter result.
        var rewrite = new NormalizedCompositionVisitor(eventData.Context);

        return rewrite.Visit(queryExpression)!;
    }

    /// <summary>Owns one compilation only; the injected singleton retains no context or expression tree.</summary>
    private sealed class NormalizedCompositionVisitor(DbContext? context) : ExpressionVisitor
    {
        private readonly NestedSetNormalizedQueryRoots _roots = new();

        /// <inheritdoc />
        public override Expression? Visit(
            Expression? node
        )
        {
            if (node is null
                || node.GetType() == typeof(QueryParameterExpression)
                || !NestedSetQuerySyntax.CanInspect(node))
            {
                return node;
            }

            return base.Visit(node);
        }

        /// <inheritdoc />
        protected override Expression VisitMethodCall(
            MethodCallExpression node
        )
        {
            if (!NestedSetDeferredQuery.IsMarker(node))
            {
                return base.VisitMethodCall(node);
            }

            // WHY: Canonicalize the exact owned bindings before visiting the inner source. EF may
            // have detached and rebuilt equivalent occurrences; no application source is interned.
            var normalized = (MethodCallExpression)base.VisitMethodCall(_roots.Canonicalize(node));
            var owner = context
                ?? throw new InvalidOperationException(
                    "Deferred hierarchy composition requires the compiling DbContext.");

            return Visit(NestedSetDeferredQuery.Expand(owner, normalized))!;
        }
    }
}

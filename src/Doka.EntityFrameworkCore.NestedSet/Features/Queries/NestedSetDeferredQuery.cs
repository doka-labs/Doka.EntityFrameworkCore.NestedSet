namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

/// <summary>Defers source-sensitive hierarchy composition until EF has normalized captured queries.</summary>
internal static partial class NestedSetDeferredQuery
{
    private static readonly ConditionalWeakTable<MethodInfo, Expansion> s_expansions = new();

    /// <summary>Recognizes only exact library definitions without reflecting over ordinary LINQ methods.</summary>
    internal static bool IsMarker(
        MethodCallExpression call
    )
    {
        // WHY: Ordinary LINQ calls dominate shared-source traversal. Reject their declaring type
        // before GetGenericMethodDefinition, whose repeated runtime wrappers otherwise allocate per visit.
        if (call.Method.GetType()
                .Assembly
            != typeof(MethodInfo).Assembly
            || call.Method.DeclaringType != typeof(NestedSetDeferredQuery)
            || !call.Method.IsGenericMethod)
        {
            return false;
        }

        var definition = call.Method.GetGenericMethodDefinition();

        return definition == s_strict
            || definition == s_inclusive
            || definition == s_path
            || definition == s_any
            || definition == s_nearest;
    }

    /// <summary>Distinguishes pair projection from the three source-preserving filter markers.</summary>
    internal static bool IsAncestorMarker(
        MethodCallExpression call
    )
    {
        var definition = call.Method.GetGenericMethodDefinition();

        return definition == s_strict || definition == s_inclusive;
    }

    /// <summary>Finds a visible library marker without inspecting captures or application extension syntax.</summary>
    internal static bool ContainsMarker(
        Expression expression
    )
    {
        var search = new MarkerSearch();
        search.Visit(expression);

        return search.Found;
    }

    /// <summary>Expands one normalized marker without retaining its compilation's context or expressions.</summary>
    internal static Expression Expand(
        DbContext context,
        MethodCallExpression marker
    ) => s_expansions
        .GetValue(marker.Method, static method => new Expansion(method))
        .Invoke(context, marker);

    /// <summary>Reads one compile-time method identity without retaining any runtime query state.</summary>
    private static MethodInfo Definition(
        string name
    ) => typeof(NestedSetDeferredQuery).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>Restores the exact mapping and normalized roots shared by every deferred operation.</summary>
    private static NestedSetQuery<TEntity> Restore<TEntity>(
        DbContext context,
        MethodCallExpression marker,
        out IQueryProvider provider
    )
        where TEntity : class
    {
        var entityName = marker.Arguments[^2] is ConstantExpression { Value: string name }
            ? name
            : throw new InvalidOperationException(
                "The deferred hierarchy mapping must remain a library-owned literal.");

        var treeBound = marker.Arguments[^1] is ConstantExpression { Value: bool bound }
            ? bound
            : throw new InvalidOperationException(
                "The deferred hierarchy identity mode must remain a library-owned literal.");

        var entityType = context.Model.FindEntityType(entityName)
            ?? throw new InvalidOperationException(
                "The deferred hierarchy mapping is not part of the compiling context.");

        if (entityType.ClrType != typeof(TEntity))
        {
            throw new InvalidOperationException(
                "The deferred hierarchy marker does not match its exact mapped entity type.");
        }

        provider = NestedSetEntityAccess<TEntity>
            .Set(context, entityType)
            .AsQueryable()
            .Provider;

        var visible = provider.CreateQuery<TEntity>(marker.Arguments[0]);
        var unbound = provider.CreateQuery<TEntity>(marker.Arguments[1]);

        return new NestedSetQuery<TEntity>(context, entityType, visible, unbound, treeBound);
    }

    /// <summary>Weakly owns one typed factory delegate, without a context, model, query, or application value.</summary>
    private sealed class Expansion
    {
        private readonly Func<DbContext, MethodCallExpression, Expression> _expand;

        /// <summary>Closes the exact operation factory once for this entity/result method identity.</summary>
        internal Expansion(
            MethodInfo marker
        )
        {
            var definition = marker.GetGenericMethodDefinition();
            var factory = definition == s_path
                ? Definition(nameof(ExpandPath))
                : definition == s_any
                    ? Definition(nameof(ExpandAny))
                    : definition == s_nearest
                        ? Definition(nameof(ExpandNearest))
                        : Definition(nameof(ExpandAncestors));

            // WHY: Compile-time generic dispatch is unavailable after EF's untyped interceptor boundary.
            // Only the cold factory closes a delegate; warm execution retains no original context or query.
            _expand = factory
                .MakeGenericMethod(marker.GetGenericArguments())
                .CreateDelegate<Func<DbContext, MethodCallExpression, Expression>>();
        }

        /// <summary>Builds this compilation's expression without retaining caller-owned objects.</summary>
        internal Expression Invoke(
            DbContext context,
            MethodCallExpression marker
        ) => new NestedSetNormalizedQueryClosure().Visit(_expand(context, marker))!;
    }

    /// <summary>Searches only runtime-owned syntax and stops at the first exact deferred operation.</summary>
    private sealed class MarkerSearch : ExpressionVisitor
    {
        /// <summary>Gets whether the inspectable source contains an exact library marker.</summary>
        internal bool Found { get; private set; }

        /// <inheritdoc />
        public override Expression? Visit(
            Expression? node
        ) => Found || node is null || !NestedSetQuerySyntax.CanInspect(node) ? node : base.Visit(node);

        /// <inheritdoc />
        protected override Expression VisitMethodCall(
            MethodCallExpression node
        )
        {
            if (IsMarker(node))
            {
                Found = true;

                return node;
            }

            return base.VisitMethodCall(node);
        }
    }
}

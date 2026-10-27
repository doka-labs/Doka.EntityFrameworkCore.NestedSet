namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

/// <summary>Checks the selected entity alias through LINQ projections without auditing payload roots.</summary>
internal static partial class NestedSetQueryEndpoint
{
    /// <summary>Rejects a constructed endpoint or an alias from another named mapping before SQL execution.</summary>
    internal static Resolution Require<TEntity, TRow>(
        IQueryable<TRow> source,
        Expression<Func<TRow, TEntity>> selector,
        IEntityType entityType,
        Expression boundSource,
        string parameterName,
        MethodInfo? sourceBindingMethod = null,
        MethodInfo? orderedJoinMethod = null
    )
        where TEntity : class
    {
        bool? sourceAllowsVisibility = null;

        return Require(
            source,
            selector,
            entityType,
            boundSource,
            parameterName,
            ref sourceAllowsVisibility,
            sourceBindingMethod,
            orderedJoinMethod);
    }

    /// <summary>Shares one source-wide visibility check between endpoint selectors in the same operation.</summary>
    /// <param name="source">The identical deferred source used by every selector sharing this check.</param>
    /// <param name="selector">The actual mapped endpoint alias to validate.</param>
    /// <param name="entityType">The exact hierarchy mapping expected by the caller.</param>
    /// <param name="boundSource">The immutable visible hierarchy expression.</param>
    /// <param name="parameterName">The argument name reported for an invalid endpoint.</param>
    /// <param name="sourceAllowsVisibility">An operation-local value, initially null for this exact source.</param>
    /// <param name="sourceBindingMethod">
    /// The validated public operator from the actual configured provider, if available.
    /// </param>
    /// <param name="orderedJoinMethod">
    /// The validated public inner-join operator from that same provider, if available.
    /// </param>
    internal static Resolution Require<TEntity, TRow>(
        IQueryable<TRow> source,
        Expression<Func<TRow, TEntity>> selector,
        IEntityType entityType,
        Expression boundSource,
        string parameterName,
        ref bool? sourceAllowsVisibility,
        MethodInfo? sourceBindingMethod = null,
        MethodInfo? orderedJoinMethod = null
    )
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(selector, parameterName);
        var actual = new EndpointResolver(boundSource, sourceBindingMethod, orderedJoinMethod).Resolve(
            source.Expression,
            selector.Parameters[0],
            selector.Body);

        var mapping = NestedSetModelMapping.For(entityType.Model);

        // WHY: Derived TPH/TPT aliases share their configured hierarchy owner. Named shared mappings
        // retain distinct owners even when their CLR type and every key happen to be identical.
        if (actual is null
            || !mapping.TryDescriptor(actual.Value.EntityType, out _)
            || !ReferenceEquals(mapping.Owner(actual.Value.EntityType), mapping.Owner(entityType)))
        {
            throw new ArgumentException(
                $"The endpoint must be a mapped entity alias of hierarchy '{entityType.Name}'. "
                + "Constructed entities and aliases of other mappings are not endpoints.",
                parameterName);
        }

        var resolved = actual.Value;

        // WHY: Only the exact immutable bound expression proves scope, tree and visibility together.
        // Equal metadata or equal-looking predicates cannot establish that an external alias is visible.
        if (!resolved.IsVisible)
        {
            return resolved;
        }

        // WHY: A path validates two endpoints of the identical source. Keep this syntax result local to
        // that operation; retaining it across calls could trust a different or subsequently captured query.
        sourceAllowsVisibility ??= VisibilityProof.Allows(
            source.Expression,
            boundSource,
            sourceBindingMethod,
            orderedJoinMethod);

        return sourceAllowsVisibility.Value ? resolved : resolved with { IsVisible = false };
    }

    /// <summary>Trusts only the validated runtime method identity, never a matching name or assembly label.</summary>
    private static bool IsValidatedProviderMethod(
        MethodCallExpression call,
        MethodInfo? method
    ) => method is not null
        && call.Method.GetType().Assembly == typeof(MethodInfo).Assembly
        && call.Method.IsGenericMethod
        && call.Method.GetGenericMethodDefinition() == method;

    /// <summary>Reads constant-backed instance fields without evaluating properties or application methods.</summary>
    private static object? ReadCapture(
        Expression expression
    )
    {
        if (expression is ConstantExpression constant)
        {
            return constant.Value;
        }

        if (expression is MemberExpression { Member: FieldInfo { IsStatic: false } field, Expression: { } owner }
            && field.GetType().Assembly == typeof(FieldInfo).Assembly
            && ReadCapture(owner) is { } target)
        {
            return field.GetValue(target);
        }

        return null;
    }

    /// <summary>Reads a quoted selector without evaluating an application delegate.</summary>
    private static LambdaExpression Lambda(
        Expression expression
    ) => (LambdaExpression)NestedSetQueryExpressions.Unwrap(expression);

    /// <summary>Reduces anonymous and simple member-initializer rows to their selected existing alias.</summary>
    private static Expression Substitute(
        ParameterExpression row,
        Expression endpoint,
        Expression projection
    ) => NestedSetQueryExpressions.Replace(endpoint, row, projection);

    /// <summary>Retains validated mapping identity separately from conservative bound-root provenance.</summary>
    /// <param name="EntityType">The actual selected mapped alias, including a narrowed inheritance mapping.</param>
    /// <param name="IsVisible">Whether this alias must originate in the exact visible bound root.</param>
    internal readonly record struct Resolution(
        IEntityType EntityType,
        bool IsVisible
    );

    /// <summary>Invalidates shortcuts for query-wide modifiers without resolving unrelated payload mappings.</summary>
    /// <param name="boundSource">The exact hierarchy binding supplied by this operation.</param>
    /// <param name="sourceBindingMethod">The validated statement-local query operator, if present.</param>
    /// <param name="orderedJoinMethod">The validated provider inner-join operator, if present.</param>
    private sealed class VisibilityProof(
        Expression boundSource,
        MethodInfo? sourceBindingMethod,
        MethodInfo? orderedJoinMethod
    ) : ExpressionVisitor
    {
        private bool _allowed = true;
        private bool _boundProved;
        private bool _checkingBound;
        private bool _canReuseBound = true;

        /// <summary>Checks every source leg while proving the exact repeated hierarchy binding only once.</summary>
        internal static bool Allows(
            Expression source,
            Expression boundSource,
            MethodInfo? sourceBindingMethod,
            MethodInfo? orderedJoinMethod
        )
        {
            var proof = new VisibilityProof(boundSource, sourceBindingMethod, orderedJoinMethod);
            proof.Visit(source);

            return proof._allowed;
        }

        /// <inheritdoc />
        public override Expression? Visit(
            Expression? node
        )
        {
            if (!_allowed
                || node is null)
            {
                return node;
            }

            var bound = _canReuseBound && ReferenceEquals(node, boundSource);

            if (bound && _boundProved)
            {
                return node;
            }

            // WHY: Only the exact hierarchy binding is reused. Its first scan must exclude application
            // expressions that can change children through Accept or VisitChildren on another visit.
            if (_canReuseBound
                && (_checkingBound || bound)
                && node.GetType().Assembly != typeof(Expression).Assembly
                && node.GetType() != typeof(EntityQueryRootExpression))
            {
                _canReuseBound = false;
            }

            return bound ? VisitBound(node) : base.Visit(node);
        }

        /// <summary>Inspects the candidate once without extending purity checks to unrelated payload syntax.</summary>
        private Expression VisitBound(
            Expression node
        )
        {
            var checkingBound = _checkingBound;
            _checkingBound = true;

            try
            {
                var visited = base.Visit(node);
                _boundProved = _allowed && _canReuseBound;

                return visited;
            }
            finally
            {
                // WHY: A custom outer expression can catch a child visitor exception and continue dispatch.
                // Restore the candidate scope even on that path; an abandoned scan is never a proof.
                _checkingBound = checkingBound;
            }
        }

        /// <inheritdoc />
        protected override Expression VisitConstant(
            ConstantExpression node
        )
        {
            if (node.Value is IQueryable)
            {
                _allowed = false;
            }

            return node;
        }

        /// <inheritdoc />
        protected override Expression VisitMember(
            MemberExpression node
        )
        {
            // WHY: Custom reflection metadata can report a different member type on another visit.
            // A candidate containing it retains the original inspection for every occurrence.
            if (_checkingBound
                && _canReuseBound
                && node.Member.GetType().Assembly != typeof(MemberInfo).Assembly)
            {
                _canReuseBound = false;
            }

            // WHY: A captured query may hide IgnoreQueryFilters, including through an application getter.
            // Retain membership rather than executing that getter or trusting a mutable captured query.
            if (CapturesQuery(node))
            {
                _allowed = false;
            }

            return base.VisitMember(node);
        }

        /// <inheritdoc />
        protected override Expression VisitUnary(
            UnaryExpression node
        )
        {
            // WHY: An object-valued property or static field can hide an arbitrary query, including
            // query-wide filter modifiers. A query conversion does not prove that carrier's contents;
            // retain membership without evaluating application getters or trusting its current value.
            if (node.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.TypeAs
                && typeof(IQueryable).IsAssignableFrom(node.Type)
                && !typeof(IQueryable).IsAssignableFrom(node.Operand.Type))
            {
                _allowed = false;
            }

            return base.VisitUnary(node);
        }

        /// <inheritdoc />
        protected override Expression VisitMethodCall(
            MethodCallExpression node
        )
        {
            if (NestedSetDeferredQuery.IsMarker(node))
            {
                // WHY: The marker preserves its mapped aliases, but a captured selection or projection
                // can still hide query-wide modifiers such as IgnoreQueryFilters. Retain the existing
                // membership proof until all those sources are known; do not evaluate their getters.
                return base.VisitMethodCall(node);
            }

            // WHY: MethodInfo is extensible too; only runtime-owned metadata proves stable modifiers.
            if (_checkingBound
                && _canReuseBound
                && node.Method.GetType().Assembly != typeof(MethodInfo).Assembly)
            {
                _canReuseBound = false;
            }

            var declaring = node.Method.DeclaringType;

            if (declaring == typeof(EntityFrameworkQueryableExtensions)
                || declaring == typeof(RelationalQueryableExtensions))
            {
                _allowed = node.Method.Name is nameof(EntityFrameworkQueryableExtensions.AsNoTracking)
                    or nameof(EntityFrameworkQueryableExtensions.AsTracking)
                    or nameof(EntityFrameworkQueryableExtensions.AsNoTrackingWithIdentityResolution)
                    or nameof(EntityFrameworkQueryableExtensions.TagWith)
                    or nameof(EntityFrameworkQueryableExtensions.TagWithCallSite)
                    or nameof(RelationalQueryableExtensions.AsSingleQuery)
                    or nameof(RelationalQueryableExtensions.AsSplitQuery);
            }
            else if (declaring == typeof(Queryable)
                     && node.Method.Name == nameof(Queryable.AsQueryable)
                     && !typeof(IQueryable).IsAssignableFrom(node.Arguments[0].Type))
            {
                // WHY: AsQueryable preserves an existing query hidden behind IEnumerable, including
                // its filter modifiers. Only an already query-typed input has inspectable provenance.
                _allowed = false;
            }
            else if (declaring != typeof(Queryable)
                     && typeof(IQueryable).IsAssignableFrom(node.Type)
                     && !IsValidatedProviderMethod(node, sourceBindingMethod)
                     && !IsValidatedProviderMethod(node, orderedJoinMethod))
            {
                _allowed = false;
            }

            return base.VisitMethodCall(node);
        }
    }
}

namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

internal static partial class NestedSetQueryEndpoint
{
    /// <summary>Traces an endpoint without copying a completed source into each lexical reference.</summary>
    private struct EndpointResolver
    {
        private readonly Expression _boundSource;
        private readonly MethodInfo? _sourceBindingMethod;
        private readonly MethodInfo? _orderedJoinMethod;

        /// <summary>
        /// Retains the exact visible source and validated provider operators for lexical resolution.
        /// </summary>
        /// <param name="boundSource">The operation's exact visible hierarchy expression.</param>
        /// <param name="sourceBindingMethod">The verified provider operator, or null for the portable path.</param>
        /// <param name="orderedJoinMethod">The verified public inner-join operator, or null when unavailable.</param>
        internal EndpointResolver(
            Expression boundSource,
            MethodInfo? sourceBindingMethod,
            MethodInfo? orderedJoinMethod
        )
        {
            _boundSource = boundSource;
            _sourceBindingMethod = sourceBindingMethod;
            _orderedJoinMethod = orderedJoinMethod;
        }

        private SourceBinding? _bindings;
        private SourceStability? _stability;
        private Dictionary<(MethodCallExpression Call, SourceBinding? Parent), SourceBinding>? _sourceBindings;

        /// <summary>
        /// Follows only the selected projection and verified lexical bindings to an actual entity root.
        /// </summary>
        internal Resolution? Resolve(
            Expression source,
            ParameterExpression row,
            Expression endpoint,
            bool visible = false
        )
        {
            // WHY: The cursor shares one lexical environment while peeling operators; recursion would grow with
            // ordinary query depth and require separate copies of that environment at every operator.
            while (true)
            {
                source = NestedSetQueryExpressions.Unwrap(source);
                endpoint = NestedSetQueryExpressions.Unwrap(endpoint);
                visible |= ReferenceEquals(source, _boundSource) && ReferenceEquals(endpoint, row);

                if (source is ParameterExpression parameter)
                {
                    for (var binding = _bindings; binding is not null; binding = binding.Parent)
                    {
                        if (!ReferenceEquals(binding.Parameter, parameter))
                        {
                            continue;
                        }

                        var stability = _stability ??= new SourceStability();
                        var cacheable = binding.CanReuse(stability) && stability.Allows(endpoint);

                        if (cacheable && binding.TryGet(row, endpoint, visible, out var cached))
                        {
                            return cached;
                        }

                        var current = _bindings;
                        _bindings = binding.Parent;

                        try
                        {
                            // WHY: The input is outside the compose lambda's scope. Restoring its parent
                            // environment also handles nested lambdas that reuse a ParameterExpression instance.
                            var result = Resolve(binding.Source, row, endpoint, visible);

                            if (cacheable)
                            {
                                binding.Set(row, endpoint, visible, result);
                            }

                            return result;
                        }
                        finally
                        {
                            _bindings = current;
                        }
                    }

                    return null;
                }

                if (source is EntityQueryRootExpression root)
                {
                    return ReferenceEquals(endpoint, row)
                        ? new Resolution(root.EntityType, visible)
                        : null;
                }

                if (source is ConstantExpression or MemberExpression
                    && ReadCapture(source) is IQueryable captured)
                {
                    // WHY: Ordinary SelectMany captures an IQueryable in a closure field, including our own
                    // single-anchor queries. Inspect its expression metadata without evaluating application getters.
                    var expression = captured.Expression;

                    if (ReferenceEquals(source, expression))
                    {
                        return null;
                    }

                    source = expression;
                    continue;
                }

                if (source is not MethodCallExpression call
                    || call.Arguments.Count == 0)
                {
                    return null;
                }

                if (NestedSetDeferredQuery.IsMarker(call))
                {
                    if (!NestedSetDeferredQuery.IsAncestorMarker(call))
                    {
                        // WHY: Deferred filters preserve every projected row alias of their exact source.
                        source = call.Arguments[2];
                        continue;
                    }

                    var projection = Lambda(call.Arguments[3]);
                    var selected = Substitute(row, endpoint, projection.Body);

                    // WHY: Both ancestor endpoints are hydrated from this exact visible hierarchy root,
                    // regardless of the later fact-selection strategy. Captured facts never supply an endpoint.
                    for (var index = 0; index < 2; index++)
                    {
                        if (NestedSetQueryExpressions.References(selected, projection.Parameters[index]))
                        {
                            return Resolve(call.Arguments[0], projection.Parameters[index], selected, visible);
                        }
                    }

                    return null;
                }

                if (IsValidatedProviderMethod(call, _sourceBindingMethod))
                {
                    var compose = Lambda(call.Arguments[1]);
                    var previous = _bindings;
                    _sourceBindings ??=
                        new Dictionary<(MethodCallExpression, SourceBinding?), SourceBinding>(
                            SourceBindingComparer.Instance);

                    var key = (call, previous);

                    if (!_sourceBindings.TryGetValue(key, out var binding))
                    {
                        binding = new SourceBinding(compose.Parameters[0], call.Arguments[0], previous);
                        _sourceBindings.Add(key, binding);
                    }

                    // WHY: Different endpoint members can revisit the same binder through separate set
                    // branches. Reuse that exact lexical scope so its per-endpoint results survive the
                    // revisit; recreating every inner scope would expand a compact shared-source DAG.
                    _bindings = binding;

                    try
                    {
                        // WHY: A binder parameter denotes its completed original input, not an unrelated query
                        // variable. Keep this association lexical so nested bindings cannot leak into other legs.
                        return Resolve(compose.Body, row, endpoint, visible);
                    }
                    finally
                    {
                        _bindings = previous;
                    }
                }

                // WHY: This exact configured public operator preserves ordinary inner-join aliases.
                // Private provider markers and same-name application methods are not provenance contracts.
                if (IsValidatedProviderMethod(call, _orderedJoinMethod))
                {
                    return ResolveJoin(call, row, endpoint, visible);
                }

                if (call.Method.DeclaringType == typeof(Queryable))
                {
                    switch (call.Method.Name)
                    {
                        case nameof(Queryable.Select):
                            {
                                var projection = Lambda(call.Arguments[1]);
                                var selected = Substitute(row, endpoint, projection.Body);

                                source = call.Arguments[0];
                                row = projection.Parameters[0];
                                endpoint = selected;
                                continue;
                            }
                        case nameof(Queryable.Join):
                        case nameof(Queryable.GroupJoin):
                        case nameof(Queryable.LeftJoin):
                        case nameof(Queryable.RightJoin):
                            {
                                return ResolveJoin(call, row, endpoint, visible);
                            }
                        case nameof(Queryable.SelectMany) when call.Arguments.Count == 2:
                            {
                                var collection = Lambda(call.Arguments[1]);

                                source = collection.Body;
                                continue;
                            }
                        case nameof(Queryable.SelectMany) when call.Arguments.Count == 3:
                            {
                                var collection = Lambda(call.Arguments[1]);
                                var projection = Lambda(call.Arguments[2]);
                                var selected = Substitute(row, endpoint, projection.Body);

                                // WHY: The result selector identifies its actual source binding. An inner endpoint
                                // must not walk an unrelated outer projection chain or inspect its captured query.
                                var outer = NestedSetQueryExpressions.References(selected, projection.Parameters[0])
                                    ? Resolve(call.Arguments[0], projection.Parameters[0], selected, visible)
                                    : null;

                                if (outer != null)
                                {
                                    return outer;
                                }

                                if (NestedSetQueryExpressions.References(selected, projection.Parameters[1]))
                                {
                                    source = collection.Body;
                                    row = projection.Parameters[1];
                                    endpoint = selected;
                                    continue;
                                }

                                return null;
                            }
                        case nameof(Queryable.Concat):
                        case nameof(Queryable.Union):
                        case nameof(Queryable.Intersect):
                        case nameof(Queryable.Except):
                            {
                                var left = Resolve(call.Arguments[0], row, endpoint, visible);
                                var right = Resolve(call.Arguments[1], row, endpoint, visible);

                                return left is { } leftResult
                                    && right is { } rightResult
                                    && ReferenceEquals(leftResult.EntityType, rightResult.EntityType)
                                        ? leftResult with { IsVisible = leftResult.IsVisible && rightResult.IsVisible }
                                        : null;
                            }
                        case nameof(Queryable.OfType):
                            {
                                var actual = Resolve(call.Arguments[0], row, endpoint, visible);
                                var narrowed = actual?.EntityType.Model.FindEntityType(
                                    call
                                        .Method
                                        .GetGenericArguments()[0]);

                                // WHY: OfType narrows a base query to a concrete TPC hierarchy; passing the base
                                // metadata through would reject the same mapped alias accepted by Set<Derived>().
                                return actual is { } resolved
                                    && narrowed is not null
                                    && resolved.EntityType.IsAssignableFrom(narrowed)
                                        ? resolved with { EntityType = narrowed }
                                        : actual;
                            }
                        case nameof(Queryable.DefaultIfEmpty):
                            {
                                var actual = Resolve(call.Arguments[0], row, endpoint, visible);

                                return actual is { } resolved
                                    ? resolved with { IsVisible = false }
                                    : null;
                            }
                        case nameof(Queryable.Cast):
                        case nameof(Queryable.Where):
                        case nameof(Queryable.OrderBy):
                        case nameof(Queryable.OrderByDescending):
                        case nameof(Queryable.ThenBy):
                        case nameof(Queryable.ThenByDescending):
                        case nameof(Queryable.Skip):
                        case nameof(Queryable.Take):
                        case nameof(Queryable.Distinct):
                        case nameof(Queryable.AsQueryable):
                            source = call.Arguments[0];
                            continue;
                    }
                }

                // WHY: Tracking, filter and relational query tags preserve the entity shape. Do not inspect every
                // root: a second named mapping may be legitimate payload while the selected endpoint belongs here.
                if (call.Method.DeclaringType == typeof(EntityFrameworkQueryableExtensions)
                    || call.Method.DeclaringType == typeof(RelationalQueryableExtensions))
                {
                    source = call.Arguments[0];
                    continue;
                }

                return null;
            }
        }

        /// <summary>
        /// Resolves one five-argument join projection while preserving optional-side membership rules.
        /// </summary>
        private Resolution? ResolveJoin(
            MethodCallExpression call,
            ParameterExpression row,
            Expression endpoint,
            bool visible
        )
        {
            // WHY: Comparer overloads have a sixth argument after the result selector. A CLR comparer
            // cannot define this relational endpoint; reject it instead of casting it as a lambda.
            if (call.Arguments.Count != 5)
            {
                return null;
            }

            var projection = Lambda(call.Arguments[4]);
            var selected = Substitute(row, endpoint, projection.Body);
            var outer = NestedSetQueryExpressions.References(selected, projection.Parameters[0])
                ? Resolve(call.Arguments[0], projection.Parameters[0], selected, visible)
                : null;

            // WHY: Only ordinary outer joins synthesize null aliases. The verified provider operator
            // is an inner join and retains the same real-endpoint visibility as ordinary Join.
            if (outer is { } outerResult)
            {
                return call.Method.DeclaringType == typeof(Queryable) && call.Method.Name == nameof(Queryable.RightJoin)
                    ? outerResult with { IsVisible = false }
                    : outerResult;
            }

            var inner = NestedSetQueryExpressions.References(selected, projection.Parameters[1])
                ? Resolve(call.Arguments[1], projection.Parameters[1], selected, visible)
                : null;

            return inner is { } innerResult
                && call.Method.DeclaringType == typeof(Queryable)
                && call.Method.Name == nameof(Queryable.LeftJoin)
                    ? innerResult with { IsVisible = false }
                    : inner;
        }
    }
}

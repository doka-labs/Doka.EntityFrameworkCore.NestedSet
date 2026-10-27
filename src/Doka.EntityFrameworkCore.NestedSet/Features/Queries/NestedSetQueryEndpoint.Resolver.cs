namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

internal static partial class NestedSetQueryEndpoint
{
    /// <summary>Traces an endpoint without copying a completed source into each lexical reference.</summary>
    /// <param name="boundSource">The operation's exact visible hierarchy expression.</param>
    /// <param name="sourceBindingMethod">The verified provider operator, or null for the portable path.</param>
    /// <param name="orderedJoinMethod">The verified public inner-join operator, or null when unavailable.</param>
    private struct EndpointResolver(
        Expression boundSource,
        MethodInfo? sourceBindingMethod,
        MethodInfo? orderedJoinMethod
    )
    {
        private SourceBinding? _bindings;
        private SourceStability? _stability;
        private Dictionary<(MethodCallExpression Call, SourceBinding? Parent), SourceBinding>? _sourceBindings;

        /// <summary>Follows only the selected projection and verified lexical bindings to an actual entity root.</summary>
        internal Resolution? Resolve(
            Expression source,
            ParameterExpression row,
            Expression endpoint,
            bool visible = false
        )
        {
            while (true)
            {
                source = NestedSetQueryExpressions.Unwrap(source);
                endpoint = NestedSetQueryExpressions.Unwrap(endpoint);
                visible |= ReferenceEquals(source, boundSource) && ReferenceEquals(endpoint, row);

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
                    return ReferenceEquals(endpoint, row) ? new Resolution(root.EntityType, visible) : null;
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

                if (IsValidatedProviderMethod(call, sourceBindingMethod))
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
                if (IsValidatedProviderMethod(call, orderedJoinMethod))
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

                                return actual is { } resolved ? resolved with { IsVisible = false } : null;
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

    /// <summary>Keys lexical scopes by exact node/environment identity without calling application equality.</summary>
    private sealed class SourceBindingComparer : IEqualityComparer<(MethodCallExpression Call, SourceBinding? Parent)>
    {
        /// <summary>Gets the stateless identity comparer shared by operation-local scope maps.</summary>
        internal static readonly SourceBindingComparer Instance = new();

        /// <inheritdoc />
        public bool Equals(
            (MethodCallExpression Call, SourceBinding? Parent) x,
            (MethodCallExpression Call, SourceBinding? Parent) y
        ) => ReferenceEquals(x.Call, y.Call) && ReferenceEquals(x.Parent, y.Parent);

        /// <inheritdoc />
        public int GetHashCode(
            (MethodCallExpression Call, SourceBinding? Parent) value
        ) => HashCode.Combine(
            RuntimeHelpers.GetHashCode(value.Call),
            value.Parent is null ? 0 : RuntimeHelpers.GetHashCode(value.Parent));
    }

    /// <summary>Associates one lexical query parameter with its input and the input's outer scope.</summary>
    /// <param name="parameter">The compose lambda's query parameter.</param>
    /// <param name="source">The completed input outside that lambda.</param>
    /// <param name="parent">The enclosing source-binding environment.</param>
    private sealed class SourceBinding(
        ParameterExpression parameter,
        Expression source,
        SourceBinding? parent
    )
    {
        /// <summary>Gets the query parameter owned by this compose lambda.</summary>
        internal ParameterExpression Parameter { get; } = parameter;

        /// <summary>Gets the completed input resolved outside the current lambda.</summary>
        internal Expression Source { get; } = source;

        /// <summary>Gets the input's enclosing lexical environment.</summary>
        internal SourceBinding? Parent { get; } = parent;

        private bool? _stable;
        private Dictionary<EndpointKey, Resolution?>? _resolutions;

        /// <summary>Proves an input and its outer environments only when an endpoint actually uses this binding.</summary>
        internal bool CanReuse(
            SourceStability stability
        )
        {
            // WHY: Late entity rehydration often resolves an endpoint through a direct mapped join.
            // Those paths need neither a stability traversal nor memo dictionaries for the scalar binding.
            _stable ??= (Parent?.CanReuse(stability) ?? true) && stability.Allows(Source);

            return _stable.Value;
        }

        /// <summary>Reuses a stable relative endpoint and incoming visibility in this lexical scope.</summary>
        internal bool TryGet(
            ParameterExpression row,
            Expression endpoint,
            bool visible,
            out Resolution? result
        )
        {
            if (_resolutions is null)
            {
                result = null;

                return false;
            }

            return _resolutions.TryGetValue(new EndpointKey(row, endpoint, visible), out result);
        }

        /// <summary>Bounds repeated shared-source resolution without introducing an inter-operation query cache.</summary>
        internal void Set(
            ParameterExpression row,
            Expression endpoint,
            bool visible,
            Resolution? result
        )
        {
            _resolutions ??= new Dictionary<EndpointKey, Resolution?>(EndpointComparer.Instance);
            _resolutions[new EndpointKey(row, endpoint, visible)] = result;
        }

        /// <summary>Recognizes alias/member paths across different lambda parameters without conflating endpoints.</summary>
        private static bool SameEndpoint(
            ParameterExpression row,
            Expression endpoint,
            ParameterExpression previousRow,
            Expression previousEndpoint
        )
        {
            while (true)
            {
                endpoint = NestedSetQueryExpressions.Unwrap(endpoint);
                previousEndpoint = NestedSetQueryExpressions.Unwrap(previousEndpoint);

                if (endpoint.Type != previousEndpoint.Type)
                {
                    return false;
                }

                if (ReferenceEquals(endpoint, row))
                {
                    return ReferenceEquals(previousEndpoint, previousRow);
                }

                // WHY: Identity Selects use different parameter instances even when they refer to the
                // same lexical input. Stable member paths can be compared relative to those parameters;
                // arbitrary constructed expressions retain reference identity and independent inspection.
                if (endpoint is MemberExpression { Expression: { } owner } member
                    && previousEndpoint is MemberExpression { Expression: { } previousOwner } previous
                    && member.Member == previous.Member)
                {
                    endpoint = owner;
                    previousEndpoint = previousOwner;
                    continue;
                }

                return ReferenceEquals(row, previousRow) && ReferenceEquals(endpoint, previousEndpoint);
            }
        }

        /// <summary>Identifies one stable relative endpoint and the visibility passed into this scope.</summary>
        private readonly record struct EndpointKey(
            ParameterExpression Row,
            Expression Endpoint,
            bool Visible
        );

        /// <summary>Shares equivalent alias paths while retaining distinct members and arbitrary expression identities.</summary>
        private sealed class EndpointComparer : IEqualityComparer<EndpointKey>
        {
            internal static readonly EndpointComparer Instance = new();

            /// <inheritdoc />
            public bool Equals(
                EndpointKey x,
                EndpointKey y
            ) => x.Visible == y.Visible && SameEndpoint(x.Row, x.Endpoint, y.Row, y.Endpoint);

            /// <inheritdoc />
            public int GetHashCode(
                EndpointKey value
            )
            {
                var hash = new HashCode();
                hash.Add(value.Visible);
                var endpoint = NestedSetQueryExpressions.Unwrap(value.Endpoint);

                while (true)
                {
                    hash.Add(endpoint.Type);

                    if (ReferenceEquals(endpoint, value.Row))
                    {
                        return hash.ToHashCode();
                    }

                    if (endpoint is MemberExpression { Expression: { } owner } member)
                    {
                        hash.Add(member.Member);
                        endpoint = NestedSetQueryExpressions.Unwrap(owner);
                        continue;
                    }

                    hash.Add(RuntimeHelpers.GetHashCode(value.Row));
                    hash.Add(RuntimeHelpers.GetHashCode(endpoint));

                    return hash.ToHashCode();
                }
            }
        }
    }
}

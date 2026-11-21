namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

internal static partial class NestedSetQueryEndpoint
{
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
            value.Parent is null
                ? 0
                : RuntimeHelpers.GetHashCode(value.Parent));
    }

    /// <summary>Associates one lexical query parameter with its input and the input's outer scope.</summary>
    private sealed class SourceBinding
    {
        /// <summary>
        /// Retains one lexical source binding without copying its parent environment.
        /// </summary>
        /// <param name="parameter">The compose lambda's query parameter.</param>
        /// <param name="source">The completed input outside that lambda.</param>
        /// <param name="parent">The enclosing source-binding environment.</param>
        internal SourceBinding(
            ParameterExpression parameter,
            Expression source,
            SourceBinding? parent
        )
        {
            Parameter = parameter;
            Source = source;
            Parent = parent;
        }

        private Dictionary<EndpointKey, Resolution?>? _resolutions;

        private bool? _stable;

        /// <summary>Gets the query parameter owned by this compose lambda.</summary>
        internal ParameterExpression Parameter { get; }

        /// <summary>Gets the completed input resolved outside the current lambda.</summary>
        internal Expression Source { get; }

        /// <summary>Gets the input's enclosing lexical environment.</summary>
        internal SourceBinding? Parent { get; }

        /// <summary>
        /// Proves an input and its outer environments only when an endpoint actually uses this binding.
        /// </summary>
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

        /// <summary>
        /// Bounds repeated shared-source resolution without introducing an inter-operation query cache.
        /// </summary>
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

        /// <summary>
        /// Recognizes alias/member paths across different lambda parameters without conflating endpoints.
        /// </summary>
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

        /// <summary>
        /// Shares equivalent alias paths while retaining distinct members and arbitrary expression identities.
        /// </summary>
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

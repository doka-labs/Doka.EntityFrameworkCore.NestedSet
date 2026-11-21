namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

/// <summary>Substitutes only the supplied lambda parameters and reduces ordinary projection members.</summary>
internal static class NestedSetQueryExpressions
{
    /// <summary>Inlines one binding without allocating an argument array.</summary>
    internal static Expression Inline(
        LambdaExpression expression,
        Expression argument
    )
    {
        // WHY: Partial and additional bindings retain the general path's existing behavior. Ordinary
        // compound selectors use compact visitors without moving the removed array into larger visitors.
        if (expression.Parameters.Count != 1)
        {
            return Inline(expression, [argument]);
        }

        // WHY: Ordinary identity/member selectors need no visitor; supplied arguments still remain unvisited.
        if (expression.Body is ParameterExpression or MemberExpression
            && TryInlineDirectBinding(expression.Body, expression.Parameters[0], argument) is { } direct)
        {
            return direct;
        }

        return new ParameterReplacement(expression.Parameters[0], argument).Visit(expression.Body);
    }

    /// <summary>Inlines two simultaneous bindings without allocating an argument array.</summary>
    internal static Expression Inline(
        LambdaExpression expression,
        Expression first,
        Expression second
    )
    {
        if (expression.Parameters.Count != 2)
        {
            return Inline(expression, [first, second]);
        }

        if (expression.Body is ParameterExpression or MemberExpression
            && (TryInlineDirectBinding(expression.Body, expression.Parameters[0], first)
                ?? TryInlineDirectBinding(expression.Body, expression.Parameters[1], second)) is { } direct)
        {
            return direct;
        }

        return new PairReplacement(expression.Parameters, first, second).Visit(expression.Body);
    }

    /// <summary>Inlines three simultaneous bindings without allocating an argument array.</summary>
    internal static Expression Inline(
        LambdaExpression expression,
        Expression first,
        Expression second,
        Expression third
    )
    {
        if (expression.Parameters.Count != 3)
        {
            return Inline(expression, [first, second, third]);
        }

        // WHY: Compound selectors skip all direct-binding lookups and retain the normal visitor path.
        if (expression.Body is ParameterExpression or MemberExpression
            && (TryInlineDirectBinding(expression.Body, expression.Parameters[0], first)
                ?? TryInlineDirectBinding(expression.Body, expression.Parameters[1], second)
                ?? TryInlineDirectBinding(expression.Body, expression.Parameters[2], third)) is { } direct)
        {
            return direct;
        }

        return new TripleReplacement(expression.Parameters, first, second, third).Visit(expression.Body);
    }

    /// <summary>Inlines an expression without compiling delegates, invoking getters, or performing I/O.</summary>
    internal static Expression Inline(
        LambdaExpression expression,
        params Expression[] arguments
    ) => new ArgumentsReplacement(expression.Parameters, arguments).Visit(expression.Body);

    /// <summary>Replaces one exact parameter without allocating a synthetic lambda or argument arrays.</summary>
    internal static Expression Replace(
        Expression expression,
        ParameterExpression parameter,
        Expression argument
    ) => new ParameterReplacement(parameter, argument).Visit(expression);

    /// <summary>Checks exact parameter identity without evaluating application expressions or captures.</summary>
    internal static bool References(
        Expression expression,
        ParameterExpression parameter
    )
    {
        var references = new ParameterReference(parameter);
        references.Visit(expression);

        return references.Found;
    }

    /// <summary>Removes quote/conversion wrappers used by ordinary query selectors.</summary>
    internal static Expression Unwrap(
        Expression expression
    )
    {
        while (expression is UnaryExpression
            {
                NodeType: ExpressionType.Quote or ExpressionType.Convert or ExpressionType.ConvertChecked,
            } unary)
        {
            expression = unary.Operand;
        }

        return expression;
    }

    /// <summary>Reuses a direct binding or its selected projection member without allocating a visitor.</summary>
    /// <param name="body">The complete selector body; compound expressions retain the visitor fallback.</param>
    /// <param name="parameter">The exact lambda parameter that this argument replaces.</param>
    /// <param name="argument">The supplied expression, which must not be recursively substituted.</param>
    /// <returns>The direct result, or null when the body requires normal expression traversal.</returns>
    private static Expression? TryInlineDirectBinding(
        Expression body,
        ParameterExpression parameter,
        Expression argument
    )
    {
        if (ReferenceEquals(body, parameter))
        {
            return argument;
        }

        if (body is MemberExpression { Expression: null or ConstantExpression })
        {
            // WHY: Static and directly captured members contain no lambda parameter and remain unevaluated.
            return body;
        }

        // WHY: Matching by identity preserves free/nested parameters that happen to share a name.
        return body is MemberExpression member && ReferenceEquals(member.Expression, parameter)
            ? ReduceMember(member, argument)
            : null;
    }

    /// <summary>Reduces the selected projection member without visiting unrelated projected values.</summary>
    private static Expression ReduceMember(
        MemberExpression node,
        Expression? target
    )
    {
        var unwrapped = target is null
            ? null
            : Unwrap(target);

        if (unwrapped is NewExpression { Members: { } members } created)
        {
            // WHY: Match constructor members with their arguments by ordinal without a projected lookup.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < members.Count; index++)
            {
                if (members[index] == node.Member)
                {
                    return created.Arguments[index];
                }
            }
        }

        if (unwrapped is MemberInitExpression initialized)
        {
            // WHY: These immutable bindings expose an interface enumerator; indexing avoids it on each member
            // reduction.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (int index = 0, count = initialized.Bindings.Count; index < count; index++)
            {
                if (initialized.Bindings[index] is MemberAssignment assignment
                    && assignment.Member == node.Member)
                {
                    return assignment.Expression;
                }
            }
        }

        return node.Update(target);
    }

    /// <summary>Shares projection member reduction across all simultaneous parameter-replacement visitors.</summary>
    private abstract class ProjectionReplacement : ExpressionVisitor
    {
        /// <inheritdoc />
        protected override Expression VisitMember(
            MemberExpression node
        ) => ReduceMember(
            node,
            node.Expression is null
                ? null
                : Visit(node.Expression));
    }

    /// <summary>Retains the general binding-list behavior for larger and mismatched argument counts.</summary>
    private sealed class ArgumentsReplacement : ProjectionReplacement
    {
        private readonly IReadOnlyList<ParameterExpression> _parameters;
        private readonly IReadOnlyList<Expression> _arguments;

        /// <summary>Retains the simultaneous parameter and argument vectors without copying them.</summary>
        internal ArgumentsReplacement(
            IReadOnlyList<ParameterExpression> parameters,
            IReadOnlyList<Expression> arguments
        )
        {
            _parameters = parameters;
            _arguments = arguments;
        }

        /// <inheritdoc />
        protected override Expression VisitParameter(
            ParameterExpression node
        )
        {
            // WHY: Match parameter identity to its replacement argument at the same ordinal.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < _parameters.Count; index++)
            {
                // WHY: Identical parameter names in nested lambdas do not mean identical bindings.
                if (ReferenceEquals(node, _parameters[index]))
                {
                    return _arguments[index];
                }
            }

            return node;
        }
    }

    /// <summary>Substitutes one exact projection binding without visiting the supplied argument.</summary>
    private sealed class ParameterReplacement : ProjectionReplacement
    {
        private readonly ParameterExpression _parameter;
        private readonly Expression _argument;

        /// <summary>Retains one exact parameter identity and its unvisited replacement expression.</summary>
        internal ParameterReplacement(
            ParameterExpression parameter,
            Expression argument
        )
        {
            _parameter = parameter;
            _argument = argument;
        }

        /// <inheritdoc />
        protected override Expression VisitParameter(
            ParameterExpression node
        ) => ReferenceEquals(node, _parameter)
            ? _argument
            : node;
    }

    /// <summary>Stores two supplied expressions directly while retaining exact parameter identity.</summary>
    private sealed class PairReplacement : ProjectionReplacement
    {
        private readonly IReadOnlyList<ParameterExpression> _parameters;
        private readonly Expression _first;
        private readonly Expression _second;

        /// <summary>Retains two simultaneous arguments without allocating a replacement array.</summary>
        internal PairReplacement(
            IReadOnlyList<ParameterExpression> parameters,
            Expression first,
            Expression second
        )
        {
            _parameters = parameters;
            _first = first;
            _second = second;
        }

        /// <inheritdoc />
        protected override Expression VisitParameter(
            ParameterExpression node
        ) => node switch
        {
            _ when ReferenceEquals(node, _parameters[0]) => _first,
            _ when ReferenceEquals(node, _parameters[1]) => _second,
            _ => node,
        };
    }

    /// <summary>Stores three supplied expressions directly without recursively replacing their bindings.</summary>
    private sealed class TripleReplacement : ProjectionReplacement
    {
        private readonly IReadOnlyList<ParameterExpression> _parameters;
        private readonly Expression _first;
        private readonly Expression _second;
        private readonly Expression _third;

        /// <summary>Retains three simultaneous arguments without allocating a replacement array.</summary>
        internal TripleReplacement(
            IReadOnlyList<ParameterExpression> parameters,
            Expression first,
            Expression second,
            Expression third
        )
        {
            _parameters = parameters;
            _first = first;
            _second = second;
            _third = third;
        }

        /// <inheritdoc />
        protected override Expression VisitParameter(
            ParameterExpression node
        )
        {
            if (ReferenceEquals(node, _parameters[0]))
            {
                return _first;
            }

            if (ReferenceEquals(node, _parameters[1]))
            {
                return _second;
            }

            return ReferenceEquals(node, _parameters[2])
                ? _third
                : node;
        }
    }

    /// <summary>Stops after finding the requested binding, including references inside nested lambdas.</summary>
    private sealed class ParameterReference : ExpressionVisitor
    {
        private readonly ParameterExpression _parameter;

        /// <summary>Retains the exact parameter identity searched during this syntax visit.</summary>
        internal ParameterReference(
            ParameterExpression parameter
        )
        {
            _parameter = parameter;
        }

        /// <summary>Gets whether the exact supplied parameter occurs in the expression.</summary>
        internal bool Found { get; private set; }

        /// <inheritdoc />
        public override Expression? Visit(
            Expression? node
        ) => Found
            ? node
            : base.Visit(node);

        /// <inheritdoc />
        protected override Expression VisitParameter(
            ParameterExpression node
        )
        {
            Found |= ReferenceEquals(node, _parameter);

            return node;
        }
    }
}

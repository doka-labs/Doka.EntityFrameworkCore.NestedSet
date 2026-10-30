namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

/// <summary>Shares equivalent normalized library bindings within one compilation, never application query trees.</summary>
internal sealed class NestedSetNormalizedQueryRoots
{
    private Dictionary<Expression, Expression>? _roots;

    /// <summary>Canonicalizes only the two owned root arguments of an already recognized exact library marker.</summary>
    internal MethodCallExpression Canonicalize(
        MethodCallExpression marker
    )
    {
        var visible = CanonicalizeRoot(marker.Arguments[0]);
        var unbound = CanonicalizeRoot(marker.Arguments[1]);

        if (ReferenceEquals(visible, marker.Arguments[0])
            && ReferenceEquals(unbound, marker.Arguments[1]))
        {
            return marker;
        }

        var arguments = marker.Arguments.ToArray();
        arguments[0] = visible;
        arguments[1] = unbound;

        return marker.Update(marker.Object, arguments);
    }

    /// <summary>Reuses a binding only after its entire grammar excludes application equality or virtual dispatch.</summary>
    private Expression CanonicalizeRoot(
        Expression root
    )
    {
        var admission = new RootAdmission();
        admission.Visit(root);

        if (!admission.Allowed)
        {
            return root;
        }

        // WHY: EF can rebuild each occurrence while replacing captures with query parameters. The
        // public comparer preserves exact entity metadata, parameter identities and every literal;
        // restoring reference sharing lets the existing endpoint proof work without a second rule.
        _roots ??= new Dictionary<Expression, Expression>(ExpressionEqualityComparer.Instance);

        if (_roots.TryGetValue(root, out var existing))
        {
            return existing;
        }

        _roots.Add(root, root);

        return root;
    }

    /// <summary>Admits only normalized binding syntax whose comparison cannot execute application code.</summary>
    private sealed class RootAdmission : ExpressionVisitor
    {
        /// <summary>Gets whether all inspected binding nodes and literal values are safe for EF's comparer.</summary>
        internal bool Allowed { get; private set; } = true;

        /// <inheritdoc />
        public override Expression? Visit(
            Expression? node
        )
        {
            if (!Allowed
                || node is null)
            {
                return node;
            }

            if (node.GetType() == typeof(EntityQueryRootExpression)
                && node is EntityQueryRootExpression root)
            {
                // WHY: Compare concrete Type implementations to exclude application-defined metadata wrappers.
                // ReSharper disable PossibleMistakenCallToGetType
                Allowed = root.QueryProvider is null
                    && root.EntityType.GetType().Assembly == typeof(IEntityType).Assembly
                    && root.Type.GetType() == typeof(object).GetType();
                // ReSharper restore PossibleMistakenCallToGetType

                return node;
            }

            if (node.GetType() == typeof(QueryParameterExpression))
            {
                // WHY: Compare concrete Type implementations to exclude application-defined metadata wrappers.
                // ReSharper disable PossibleMistakenCallToGetType
                Allowed = node.Type.GetType() == typeof(object).GetType();
                // ReSharper restore PossibleMistakenCallToGetType

                return node;
            }

            if (!NestedSetQuerySyntax.CanInspect(node)
                || node is not (MethodCallExpression
                    or UnaryExpression
                    or BinaryExpression
                    or LambdaExpression
                    or ParameterExpression
                    or MemberExpression
                    or ConstantExpression)
                || (node is BinaryExpression { Method: { } binary }
                    && binary.GetType().Assembly != typeof(MethodInfo).Assembly)
                || (node is UnaryExpression { Method: { } unary }
                    && unary.GetType().Assembly != typeof(MethodInfo).Assembly))
            {
                Allowed = false;

                return node;
            }

            base.Visit(node);

            // WHY: Runtime parents can forward a child's Type. Inspect children first so no opaque
            // child or application metadata can be reached indirectly through that otherwise safe parent.
            // WHY: Compare concrete Type implementations to exclude application-defined metadata wrappers.
            // ReSharper disable PossibleMistakenCallToGetType
            if (Allowed && node.Type.GetType() != typeof(object).GetType())
            {
                Allowed = false;
            }

            // ReSharper restore PossibleMistakenCallToGetType

            return node;
        }

        /// <inheritdoc />
        protected override Expression VisitConstant(
            ConstantExpression node
        )
        {
            // WHY: EF's comparer invokes literal Equals/GetHashCode and structural-array callbacks.
            // Custom objects, structs and arrays are deliberately not interned; captured values remain
            // EF parameters, so rejecting those literals retains correctness and the conservative proof.
            var value = node.Value;
            Allowed = value is null
                or string
                or bool
                or char
                or byte
                or sbyte
                or short
                or ushort
                or int
                or uint
                or long
                or ulong
                or float
                or double
                or decimal
                or Guid
                or DateTime
                or DateTimeOffset
                or DateOnly
                or TimeOnly
                or TimeSpan
                or DbFunctions;

            return node;
        }
    }
}

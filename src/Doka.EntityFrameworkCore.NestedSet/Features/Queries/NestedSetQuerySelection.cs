namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

/// <summary>Leaves explicitly query-bearing anchor predicates available for database decorrelation.</summary>
internal sealed partial class NestedSetQuerySelection : ExpressionVisitor
{
    private bool _usesQueryOperators;
    private readonly ParameterExpression _entity;

    private NestedSetQuerySelection(
        ParameterExpression entity
    )
    {
        _entity = entity;
    }

    /// <summary>Checks expression syntax without executing captured queries or application getters.</summary>
    internal static bool UsesQueryOperators<TEntity>(
        Expression<Func<TEntity, bool>> predicate
    )
    {
        var selection = new NestedSetQuerySelection(predicate.Parameters[0]);
        selection.Visit(predicate.Body);

        return selection._usesQueryOperators;
    }

    /// <inheritdoc />
    public override Expression? Visit(
        Expression? node
    )
    {
        if (_usesQueryOperators || node is null)
        {
            return node;
        }

        if (node.GetType() == typeof(QueryParameterExpression))
        {
            _usesQueryOperators = typeof(IQueryable).IsAssignableFrom(node.Type);

            return node;
        }

        if (!CanInspectSyntax(node))
        {
            _usesQueryOperators = true;

            return node;
        }

        base.Visit(node);

        // WHY: Framework parents can forward a child's Type. Inspect children first so retained
        // application metadata cannot run a virtual getter through an otherwise ordinary parent.
        // WHY: Compare concrete Type implementations to exclude application-defined metadata wrappers.
        // ReSharper disable PossibleMistakenCallToGetType
        if (!_usesQueryOperators
            && node.Type.GetType() != typeof(object).GetType())
        {
            _usesQueryOperators = true;
        }

        // ReSharper restore PossibleMistakenCallToGetType

        return node;
    }

    /// <inheritdoc />
    protected override Expression VisitMethodCall(
        MethodCallExpression node
    )
    {
        if (node.Method.DeclaringType == typeof(Queryable)
            || (node.Method.DeclaringType == typeof(Enumerable)
                && node.Method.Name != nameof(Enumerable.Contains)
                && ReferencesOrIsOpaque(node, _entity)))
        {
            _usesQueryOperators = true;

            return node;
        }

        return base.VisitMethodCall(node);
    }

    /// <inheritdoc />
    protected override Expression VisitMember(
        MemberExpression node
    )
    {
        base.VisitMember(node);

        if (_usesQueryOperators)
        {
            return node;
        }

        // WHY: EF can normalize a navigation's Count into a relational subquery. A scalar property merely
        // named Count/Length, a string length, or a captured collection count is not such a dependency.
        if (node.Member.Name == nameof(ICollection.Count)
            && node.Expression is { } collection
            && typeof(IEnumerable).IsAssignableFrom(collection.Type)
            && ReferencesOrIsOpaque(collection, _entity))
        {
            _usesQueryOperators = true;
        }

        return node;
    }

    /// <inheritdoc />
    protected override Expression VisitUnary(
        UnaryExpression node
    )
    {
        if (node.NodeType == ExpressionType.ArrayLength
            && ReferencesOrIsOpaque(node.Operand, _entity))
        {
            _usesQueryOperators = true;

            return node;
        }

        return base.VisitUnary(node);
    }

    /// <summary>Recognizes syntax and metadata whose visitor dispatch cannot execute application code.</summary>
    // WHY: New/indexer nodes can retain application reflection objects. Dynamic dispatch reduces through
    // extension visitation and can inspect an application-supplied delegate Type before visiting children.
    private static bool CanInspectSyntax(
        Expression node
    ) => NestedSetQuerySyntax.CanInspect(node);

    /// <summary>Retains unknown child syntax as dependent without invoking its traversal.</summary>
    private static bool ReferencesOrIsOpaque(
        Expression node,
        ParameterExpression parameter
    )
    {
        var reference = new SelectionReference(parameter);
        reference.Visit(node);

        return reference.FoundOrOpaque;
    }

    /// <summary>Checks row dependencies within the surrounding visitors' syntax ownership boundary.</summary>
    private sealed class SelectionReference(ParameterExpression parameter) : ExpressionVisitor
    {
        /// <summary>Gets whether a parameter reference or opaque child prevents an independence proof.</summary>
        internal bool FoundOrOpaque { get; private set; }

        /// <inheritdoc />
        public override Expression? Visit(
            Expression? node
        )
        {
            if (FoundOrOpaque || node is null)
            {
                return node;
            }

            if (node.GetType() == typeof(QueryParameterExpression))
            {
                return node;
            }

            if (!CanInspectSyntax(node))
            {
                FoundOrOpaque = true;

                return node;
            }

            return base.Visit(node);
        }

        /// <inheritdoc />
        protected override Expression VisitParameter(
            ParameterExpression node
        )
        {
            FoundOrOpaque = ReferenceEquals(node, parameter);

            return node;
        }
    }
}

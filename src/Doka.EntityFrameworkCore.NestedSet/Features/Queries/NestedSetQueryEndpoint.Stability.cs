namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

internal static partial class NestedSetQueryEndpoint
{
    /// <summary>Detects captured query fields even when their declared type hides the runtime query.</summary>
    private static bool CapturesQuery(
        MemberExpression member
    )
    {
        if (typeof(IQueryable).IsAssignableFrom(member.Type))
        {
            return true;
        }

        // WHY: Object and interface fields can hold a query with a changing Expression getter or
        // hidden filter modifiers. Read only constant-backed runtime fields, without invoking application
        // properties or excluding ordinary object payloads from the stable shared-source cache.
        return !member.Type.IsSealed && ReadCapture(member) is IQueryable;
    }

    /// <summary>Proves stable expression metadata once per node without trusting captured query getters.</summary>
    private sealed class SourceStability : ExpressionVisitor
    {
        private readonly Dictionary<Expression, bool> _nodes = new(ReferenceEqualityComparer.Instance);
        private bool _stable;

        /// <summary>Checks immutable syntax before memoizing a lexical mapping resolution.</summary>
        internal bool Allows(
            Expression source
        )
        {
            _stable = true;
            Visit(source);

            return _stable;
        }

        /// <inheritdoc />
        public override Expression? Visit(
            Expression? node
        )
        {
            if (!_stable
                || node is null)
            {
                return node;
            }

            if (_nodes.TryGetValue(node, out var stable))
            {
                _stable = stable;

                return node;
            }

            if (node.GetType().Assembly != typeof(Expression).Assembly
                && node.GetType() != typeof(EntityQueryRootExpression))
            {
                _stable = false;
            }

            // WHY: A query-valued field can expose an IQueryable whose Expression getter changes
            // between references. Custom expression/reflection metadata has the same risk. Such
            // inputs retain individual inspection; only runtime-owned stable syntax is memoized.
            if (node is ConstantExpression { Value: IQueryable }
                || (node is MemberExpression member
                    && (member.Member.GetType().Assembly != typeof(MemberInfo).Assembly
                        || CapturesQuery(member)))
                || (node is MethodCallExpression call
                    && call.Method.GetType().Assembly != typeof(MethodInfo).Assembly))
            {
                _stable = false;
            }

            if (_stable)
            {
                base.Visit(node);
            }

            _nodes.Add(node, _stable);

            return node;
        }
    }
}

using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class CompositionAncestorIndexTests
{
    /// <summary>Finds complete finite interval checks between structural aliases before entity hydration.</summary>
    private sealed class StrictCoordinateContainmentCounter : ExpressionVisitor
    {
        /// <summary>Gets the number of structural filters retaining all four strict comparisons.</summary>
        internal int CompletePredicates { get; private set; }

        /// <inheritdoc />
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(Queryable)
                && node.Method.Name == nameof(Queryable.Where)
                && node.Arguments[1] is UnaryExpression { Operand: LambdaExpression predicate })
            {
                var bounds = new CoordinateComparisonCounter();
                bounds.Visit(predicate.Body);

                if (bounds.HasCompleteStrictBounds)
                {
                    CompletePredicates++;
                }
            }

            return base.VisitMethodCall(node);
        }
    }

    /// <summary>Distinguishes scalar coordinate comparisons from mapped aliases and envelope-only limits.</summary>
    private sealed class CoordinateComparisonCounter : ExpressionVisitor
    {
        private static readonly (ExpressionType Kind, string Left, string Right)[] s_required =
        [
            (ExpressionType.LessThan, "Left", "Left"),
            (ExpressionType.GreaterThan, "Right", "Right"),
            (ExpressionType.LessThan, "Left", "Right"),
            (ExpressionType.GreaterThan, "Right", "Left"),
        ];

        private readonly HashSet<(ExpressionType Kind, string Left, string Right)> _comparisons = [];

        /// <summary>Gets whether a structural relation enforces every original strict finite bound.</summary>
        internal bool HasCompleteStrictBounds => _comparisons.IsSupersetOf(s_required);

        /// <inheritdoc />
        protected override Expression VisitBinary(BinaryExpression node)
        {
            // WHY: Mapped endpoints can use ordinary CLR members, so their final guard cannot
            // prove rejected candidates avoided hydration. Exclude this model's entity aliases;
            // an envelope's MaxLeft/MinRight alone is only a necessary prefilter.
            if (node.Left is MemberExpression { Expression: MemberExpression leftAlias } left
                && node.Right is MemberExpression { Expression: MemberExpression rightAlias } right
                && left.Type == typeof(long) && right.Type == typeof(long)
                && leftAlias.Type != typeof(CompositionNode) && rightAlias.Type != typeof(CompositionNode)
                && leftAlias.Expression is ParameterExpression parameter
                && ReferenceEquals(parameter, rightAlias.Expression)
                && leftAlias.Member != rightAlias.Member)
            {
                _comparisons.Add((node.NodeType, left.Member.Name, right.Member.Name));
            }

            return base.VisitBinary(node);
        }
    }
}

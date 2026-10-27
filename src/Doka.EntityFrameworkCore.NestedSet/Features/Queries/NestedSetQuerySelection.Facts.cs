namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

internal sealed partial class NestedSetQuerySelection
{
    /// <summary>Allows explicit fact roots while keeping hierarchy pivots and opaque sources decorrelatable.</summary>
    /// <param name="predicate">The selected node decision, including any privately composed fact expression.</param>
    /// <param name="model">The actual context model identifying hierarchy roots and navigation targets.</param>
    /// <param name="factSelection">Whether independent facts need their own inner source query block.</param>
    /// <returns>Whether completing this source could fence a hierarchy-dependent direction change.</returns>
    internal static bool RequiresAncestorDecorrelation<TEntity>(
        Expression<Func<TEntity, bool>> predicate,
        IModel model,
        out bool factSelection
    )
    {
        factSelection = false;

        if (!UsesQueryOperators(predicate))
        {
            return false;
        }

        // WHY: A fact EXISTS can select sparse measured nodes without containing a hierarchy pivot.
        // Only explicit EF roots prove that distinction. Captured query getters and hierarchy-backed
        // filters retain the conservative path rather than being evaluated to guess their contents.
        var facts = new FactSelection(model, predicate.Parameters[0]);
        facts.Visit(predicate.Body);
        factSelection = !facts.RequiresCorrelation && facts.HasFactRoot;

        return !factSelection;
    }

    /// <summary>Inspects an owned fact expression without retaining its model or evaluating application code.</summary>
    private sealed class FactSelection(
        IModel model,
        ParameterExpression selected
    ) : ExpressionVisitor
    {
        private HashSet<IEntityType>? _filteredTypes;

        /// <summary>Gets whether an unknown or hierarchy-dependent source prevents independent completion.</summary>
        internal bool RequiresCorrelation { get; private set; }

        /// <summary>Gets whether at least one concrete non-hierarchy EF root was inspected.</summary>
        internal bool HasFactRoot { get; private set; }

        /// <inheritdoc />
        public override Expression? Visit(Expression? node)
        {
            if (RequiresCorrelation || node is null)
            {
                return node;
            }

            if (node.GetType() == typeof(QueryParameterExpression))
            {
                // WHY: EF parameters are immutable scalar leaves, not reducible application syntax.
                // A query-valued parameter still cannot prove an independent fact source.
                RequiresCorrelation = typeof(IQueryable).IsAssignableFrom(node.Type);

                return node;
            }

            if (node.GetType() == typeof(EntityQueryRootExpression) && node is EntityQueryRootExpression root)
            {
                InspectRoot(root.EntityType);

                return node;
            }

            if (!CanInspectSyntax(node))
            {
                RequiresCorrelation = true;

                return node;
            }

            base.Visit(node);

            if (RequiresCorrelation)
            {
                return node;
            }

            // WHY: A retained Type wrapper can execute application code in IsAssignableFrom. The
            // child-first pass also prevents a parent's Type getter from reaching opaque child metadata.
            var type = node.Type;

            if (type.GetType() != typeof(object).GetType()
                || (typeof(IQueryable).IsAssignableFrom(type)
                    && (node is not MethodCallExpression call || !IsFactSourceOperator(call.Method))))
            {
                RequiresCorrelation = true;
            }

            return node;
        }

        /// <inheritdoc />
        protected override Expression VisitConstant(ConstantExpression node)
        {
            RequiresCorrelation = node.Value is IQueryable;

            return node;
        }

        /// <inheritdoc />
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            base.VisitMethodCall(node);

            if (RequiresCorrelation)
            {
                return node;
            }

            if (node.Method.DeclaringType == typeof(Enumerable)
                && node.Method.Name != nameof(Enumerable.Contains)
                && ReferencesOrIsOpaque(node, selected))
            {
                RequiresCorrelation = true;

                return node;
            }

            if (node.Method.DeclaringType == typeof(EF) && node.Method.Name == nameof(EF.Property)
                && (node.Arguments[1] is not ConstantExpression { Value: string property }
                    || model.FindEntityType(node.Arguments[0].Type)?.FindProperty(property) is null))
            {
                RequiresCorrelation = true;

                return node;
            }

            return node;
        }

        /// <inheritdoc />
        protected override Expression VisitMember(MemberExpression node)
        {
            base.VisitMember(node);

            if (RequiresCorrelation)
            {
                return node;
            }

            if (node.Member.Name == nameof(ICollection.Count)
                && node.Expression is { } collection
                && typeof(IEnumerable).IsAssignableFrom(collection.Type)
                && ReferencesOrIsOpaque(collection, selected))
            {
                RequiresCorrelation = true;

                return node;
            }

            // WHY: A navigation can hide a hierarchy behind the target's filters or another navigation.
            // Keep those sources decorrelatable instead of expanding their model-dependent implicit roots.
            if (node.Expression is { } owner
                && model.FindEntityType(owner.Type) is { } declaring
                && (declaring.FindNavigation(node.Member.Name) is not null
                    || declaring.FindSkipNavigation(node.Member.Name) is not null))
            {
                RequiresCorrelation = true;

                return node;
            }

            return node;
        }

        /// <summary>Admits ordinary LINQ and tracking-only markers, never arbitrary query-producing methods.</summary>
        private static bool IsFactSourceOperator(MethodInfo method)
            => method.DeclaringType == typeof(Queryable)
                || (method.DeclaringType == typeof(EntityFrameworkQueryableExtensions)
                    && method.Name is nameof(EntityFrameworkQueryableExtensions.AsNoTracking)
                        or nameof(EntityFrameworkQueryableExtensions.AsTracking)
                        or nameof(EntityFrameworkQueryableExtensions.AsNoTrackingWithIdentityResolution));

        /// <inheritdoc />
        protected override Expression VisitUnary(UnaryExpression node)
        {
            if (node.NodeType == ExpressionType.ArrayLength
                && ReferencesOrIsOpaque(node.Operand, selected))
            {
                RequiresCorrelation = true;

                return node;
            }

            return base.VisitUnary(node);
        }

        /// <summary>Checks inherited filters too, because EF applies them after selection construction.</summary>
        private void InspectRoot(IEntityType entity)
        {
            if (!ReferenceEquals(entity.Model, model) || IsHierarchy(entity))
            {
                RequiresCorrelation = true;

                return;
            }

            HasFactRoot = true;

            for (var current = entity; current is not null; current = current.BaseType)
            {
                if (_filteredTypes?.Contains(current) == true)
                {
                    continue;
                }

                foreach (var filter in current.GetDeclaredQueryFilters())
                {
                    _filteredTypes ??= new HashSet<IEntityType>(ReferenceEqualityComparer.Instance);
                    _filteredTypes.Add(current);
                    Visit(filter.Expression);
                }
            }
        }

        /// <summary>Recognizes inherited hierarchy configuration without manufacturing CLR-type identity.</summary>
        private static bool IsHierarchy(IEntityType entity)
        {
            for (var current = entity; current is not null; current = current.BaseType)
            {
                if (NestedSetModelValidator.IsNestedSet(current))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

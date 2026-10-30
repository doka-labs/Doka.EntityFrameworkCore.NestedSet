namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

internal sealed partial class NestedSetQuery<TEntity>
{
    /// <summary>Retains immutable structural projections for one exact finalized entity mapping.</summary>
    private sealed class AncestorIndexCoordinateProjections
    {
        private static readonly ConditionalWeakTable<IEntityType, AncestorIndexCoordinateProjections> s_cache = new();

        /// <summary>Gets the source selector without a generated flag that its consumer does not read.</summary>
        internal Expression<Func<TEntity, IndexedAncestorCoordinates>> Source { get; }

        /// <summary>Gets the candidate selector with the exact configured generated Boolean.</summary>
        internal Expression<Func<TEntity, IndexedAncestorCoordinates>> Candidate { get; }

        /// <summary>Creates context-free selectors without closing over mutable property-name holders.</summary>
        private AncestorIndexCoordinateProjections(
            NestedSetModelDescriptor descriptor,
            IEntityType entityType
        )
        {
            var nonLeaf = descriptor.AncestorIndexProperty
                ?? throw new InvalidOperationException("Ancestor coordinate projections require an ancestor index.");

            Source = Create(descriptor, entityType, nonLeaf: null);
            Candidate = Create(descriptor, entityType, nonLeaf);
        }

        /// <summary>Publishes selectors without rooting a context, request, model, or another named mapping.</summary>
        internal static AncestorIndexCoordinateProjections For(
                IEntityType entityType
            )
            // WHY: A CLR type can have different roles in named mappings and later finalized models.
            // The weak exact metadata key preserves those boundaries and allows the entire model to collect.
            => s_cache.GetValue(
                entityType,
                static entity => new AncestorIndexCoordinateProjections(
                    NestedSetModelMapping
                        .For(entity.Model)
                        .Descriptor(entity),
                    entity));

        /// <summary>Builds the original narrow role order using constant EF translation markers.</summary>
        private static Expression<Func<TEntity, IndexedAncestorCoordinates>> Create(
            NestedSetModelDescriptor descriptor,
            IEntityType entityType,
            NestedSetPropertyDescriptor? nonLeaf
        )
        {
            var entity = Expression.Parameter(typeof(TEntity), "entity");
            var bindings = new List<MemberBinding>(6)
            {
                Bind(entity, nameof(IndexedAncestorCoordinates.Key), descriptor.NodeKey, typeof(object)),
                Bind(entity, nameof(IndexedAncestorCoordinates.Tree), descriptor.TreeId, typeof(object)),
            };

            // WHY: Omitting an unconfigured Scope avoids an object/null relational projection that
            // breaks unscoped set operations and collection identity. Object EF markers retain converters.
            if (descriptor.Scope is { } scope)
            {
                bindings.Add(Bind(entity, nameof(IndexedAncestorCoordinates.Scope), scope, typeof(object)));
            }

            // WHY: Exact mapped numeric members translate to the same columns without EF.Property's
            // repeated method-argument traversal. Shadow/indexer geometry retains its existing marker;
            // object-valued identities above keep their native converter and equality semantics.
            bindings.Add(
                BindMapped(entity, nameof(IndexedAncestorCoordinates.Left), descriptor.Left.Resolve(entityType)));
            bindings.Add(
                BindMapped(entity, nameof(IndexedAncestorCoordinates.Right), descriptor.Right.Resolve(entityType)));

            if (nonLeaf is { } flag)
            {
                bindings.Add(Bind(entity, nameof(IndexedAncestorCoordinates.NonLeaf), flag, typeof(bool)));
            }

            // WHY: Literal configured names avoid repeated closure evaluation during every warm EF
            // parameter pass. The cache retains only expressions and immutable names, never a query root.
            return Expression.Lambda<Func<TEntity, IndexedAncestorCoordinates>>(
                Expression.MemberInit(Expression.New(typeof(IndexedAncestorCoordinates)), bindings),
                entity);
        }

        /// <summary>Preserves the existing translation-only access type instead of invoking a CLR accessor.</summary>
        private static MemberAssignment Bind(
            ParameterExpression entity,
            string member,
            NestedSetPropertyDescriptor property,
            Type markerType
        ) => Expression.Bind(
            typeof(IndexedAncestorCoordinates).GetProperty(member)!,
            NestedSetExpressions.Property(entity, property.Name, markerType));

        /// <summary>Uses exact numeric members with a marker fallback for other storage shapes.</summary>
        private static MemberAssignment BindMapped(
            ParameterExpression entity,
            string member,
            IProperty property
        ) => Expression.Bind(
            typeof(IndexedAncestorCoordinates).GetProperty(member)!,
            NestedSetExpressions.QueryProperty(entity, property));
    }
}

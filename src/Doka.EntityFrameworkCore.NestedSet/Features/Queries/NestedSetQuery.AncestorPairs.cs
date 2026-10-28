namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

internal sealed partial class NestedSetQuery<TEntity>
{
    /// <summary>Matches sources to independent nonleaf coordinates before rehydrating mapped ancestors.</summary>
    private IQueryable<TResult> SelectIndependentAncestorPairs<TResult>(
        Expression<Func<TEntity, bool>> selected,
        Expression<Func<TEntity, TEntity, int, TResult>> projection
    )
    {
        if (_capabilities.CompletedSourceMethod is null)
        {
            return SelectCoordinateAncestorPairs(selected, projection);
        }

        if (SelectBoundedIndependentAncestorPairs(selected, projection) is { } bounded)
        {
            return bounded;
        }

        var node = Expression.Parameter(typeof(TEntity), "node");
        var nonLeaf = Expression.Lambda<Func<TEntity, bool>>(NonLeafBounds(node), node);

        var keyName = _descriptor.NodeKey.Name;
        var treeName = _descriptor.TreeId.Name;
        var leftName = _descriptor.Left.Name;
        var rightName = _descriptor.Right.Name;

        // WHY: Fence only structural columns. A full entity DISTINCT also demanded unrelated payload
        // columns and could materialize large application values merely to test four scalar roles.
        var coordinates = _nodes
            .Where(nonLeaf)
            .Select(entity => new AncestorCoordinates
            {
                Key = EF.Property<object>(entity, keyName),
                Tree = EF.Property<object>(entity, treeName),
                Left = EF.Property<long>(entity, leftName),
                Right = EF.Property<long>(entity, rightName),
            })
            .Distinct();

        // WHY: Embed this owned expression rather than capturing its IQueryable. A captured query
        // correctly invalidates source-wide visibility proof because a caller could hide filter modifiers.
        var collection = Expression.Lambda<Func<TEntity, IEnumerable<AncestorCoordinates>>>(
            coordinates.Expression,
            Expression.Parameter(typeof(TEntity), "source"));

        var ranges = _nodes
            .Where(selected)
            .SelectMany(
                collection,
                (source, coordinate) => new AncestorCoordinatePair
                {
                    Source = source,
                    Coordinate = coordinate,
                });

        var occurrence = Expression.Parameter(typeof(AncestorCoordinatePair), "pair");
        var sourceNode = Expression.Property(occurrence, nameof(AncestorCoordinatePair.Source));
        var ancestorCoordinates = Expression.Property(occurrence, nameof(AncestorCoordinatePair.Coordinate));
        var containment = ContainedBounds(
            Expression.Property(ancestorCoordinates, nameof(AncestorCoordinates.Left)),
            Expression.Property(ancestorCoordinates, nameof(AncestorCoordinates.Right)),
            Bound(sourceNode, _descriptor.Left),
            Bound(sourceNode, _descriptor.Right),
            includeSelf: false);

        if (!_treeBound)
        {
            containment = Expression.AndAlso(
                Expression.Equal(
                    Expression.Property(ancestorCoordinates, nameof(AncestorCoordinates.Tree)),
                    NestedSetExpressions.Property(sourceNode, _descriptor.TreeId.Resolve(_entityType), typeof(object))),
                containment);
        }

        var onPath = Expression.Lambda<Func<AncestorCoordinatePair, bool>>(containment, occurrence);
        Expression<Func<TEntity, object>> key = entity => EF.Property<object>(entity, keyName);

        // WHY: Keep coordinates independent until their range matches. Joining mapped owners earlier
        // let Doka reorder their full population before the nonleaf fence. This final identity join
        // restores actual mapped aliases and collection identifiers without deduplicating caller values.
        var pairs = ranges
            .Where(onPath)
            .Join(
                _nodes,
                pair => pair.Coordinate.Key,
                key,
                (pair, related) => new
                {
                    Source = pair.Source,
                    Related = related,
                });

        var source = Expression.Parameter(typeof(TEntity), "source");
        var ancestor = Expression.Parameter(typeof(TEntity), "ancestor");
        var distance = Expression.Parameter(typeof(int), "distance");
        var upwardProjection = Expression.Lambda<Func<TEntity, TEntity, int, TResult>>(
            NestedSetQueryExpressions.Inline(projection, source, ancestor, distance),
            ancestor,
            source,
            distance);

        // WHY: Structural upper/lower roles reverse for an upward relation; caller arguments do not.
        return ProjectPairs(pairs, pair => pair.Related, pair => pair.Source, upwardProjection);
    }

    /// <summary>Bounds independent candidates by one completed source without requiring a generated index.</summary>
    /// <typeparam name="TResult">The caller's projected source and ancestor result.</typeparam>
    /// <param name="selected">The source predicate whose independent relation must be completed once.</param>
    /// <param name="projection">The caller projection over exact mapped source and ancestor aliases.</param>
    /// <returns>The deferred bounded query, or null when correlation or adapter primitives require the portable path.</returns>
    private IQueryable<TResult>? SelectBoundedIndependentAncestorPairs<TResult>(
        Expression<Func<TEntity, bool>> selected,
        Expression<Func<TEntity, TEntity, int, TResult>> projection
    )
    {
        // WHY: A relational pivot must remain available to a later direction change. Adapters without
        // source completion keep the portable query instead of evaluating a volatile source twice.
        if (NestedSetQuerySelection.RequiresAncestorDecorrelation(selected, _entityType.Model, out var factSelection)
            || _capabilities.CompletedSourceMethod is null
            || _capabilities.OrderedJoinMethod is null)
        {
            return null;
        }

        // WHY: This path can be added after EF has extracted query parameters. Literal mapped names
        // and exact numeric members remain SQL projection roles; name closures would become deferred
        // client EF.Property calls that the provider correctly refuses to materialize as source columns.
        var coordinates = IndependentAncestorCoordinates();

        // WHY: Complete only structural identity and interval roles. Payload is restored through the visible
        // mapped root after matching, so a broad source never materializes unrelated application columns.
        var sources = _nodes
            .Where(selected)
            .Select(coordinates);

        if (factSelection)
        {
            // WHY: An independent fact EXISTS still needs its own completed query block. Otherwise,
            // MariaDB can merge the selection into the recursive anchor and scan every hierarchy node.
            // These roles include the complete scope-local unique identity, so no source is lost.
            sources = sources.Distinct();
        }

        var sourceParameter = Expression.Parameter(typeof(IQueryable<AncestorCoordinates>), "selectedSources");
        var sharedSources = _nodes.Provider.CreateQuery<AncestorCoordinates>(sourceParameter);
        var envelopes = _descriptor.Scope is null
            ? sharedSources
                .GroupBy(source => source.Tree)
                .Select(group => new AncestorEnvelope
                {
                    Tree = group.Key,
                    MaxLeft = group.Max(source => source.Left),
                    MinRight = group.Min(source => source.Right),
                })
            : sharedSources
                .GroupBy(source => new
                {
                    source.Scope,
                    source.Tree,
                })
                .Select(group => new AncestorEnvelope
                {
                    Scope = group.Key.Scope,
                    Tree = group.Key.Tree,
                    MaxLeft = group.Max(source => source.Left),
                    MinRight = group.Min(source => source.Right),
                });

        var node = Expression.Parameter(typeof(TEntity), "node");
        var nonLeaf = Expression.Lambda<Func<TEntity, bool>>(NonLeafBounds(node), node);

        // WHY: Constant library prefixes made MariaDB scan every owner before applying the selected
        // interval. Keep model visibility filters, but derive all partition points from the source so
        // the existing Right index can range-check each envelope without a generated nonleaf column.
        var owners = _unboundNodes
            .Where(nonLeaf)
            .Select(coordinates);

        var candidates = envelopes
            .Join(
                owners,
                envelope => true,
                owner => true,
                (envelope, owner) => new
                {
                    Envelope = envelope,
                    Owner = owner,
                })
            .Where(pair => NestedSetNativePointRange<object>.Contains(pair.Owner.Tree, pair.Envelope.Tree)
                && pair.Owner.Left < pair.Envelope.MaxLeft
                && pair.Owner.Right > pair.Envelope.MinRight);

        if (_descriptor.Scope is not null)
        {
            candidates = candidates.Where(pair => NestedSetNativePointRange<object?>.Contains(
                pair.Owner.Scope,
                pair.Envelope.Scope));
        }

        var candidateCoordinates = candidates
            .Select(pair => pair.Owner)
            .Distinct();

        // WHY: The envelope is necessary, not sufficient. Keep all four original interval checks per
        // occurrence; grouping by tree also makes an empty source produce no candidates without sentinels.
        var keys = sharedSources
            .Join(
                candidateCoordinates,
                source => source.Tree,
                owner => owner.Tree,
                (source, owner) => new
                {
                    Source = source,
                    Owner = owner,
                })
            .Where(pair =>
                pair.Owner.Left < pair.Source.Left
                && pair.Owner.Right > pair.Source.Right
                && pair.Source.Left < pair.Owner.Right
                && pair.Source.Right > pair.Owner.Left)
            .Select(pair => new AncestorIndexKeyPair
            {
                Source = pair.Source.Key,
                Related = pair.Owner.Key,
            });

        // WHY: MySQL pulls nested EXISTS joins into one optimizer search and loses their hydration
        // dependencies. Complete matched identities there; MariaDB does not need this extra boundary,
        // which exhausted its sample container during branched chains. Neither path deduplicates pairs.
        var completeMatches = _capabilities.RequiresCompletedAncestorMatches(_context);
        var matchParameter = Expression.Parameter(typeof(IQueryable<AncestorIndexKeyPair>), "matchedAncestors");
        var sharedMatches = completeMatches ? _nodes.Provider.CreateQuery<AncestorIndexKeyPair>(matchParameter) : keys;

        var entity = Expression.Parameter(typeof(TEntity), "entity");
        var key = Expression.Lambda<Func<TEntity, object>>(
            NestedSetExpressions.Property(entity, _descriptor.NodeKey.Resolve(_entityType), typeof(object)),
            entity);

        var sourceNodes = sharedMatches.Join(
            _nodes,
            pair => pair.Source,
            key,
            (pair, source) => new
            {
                Source = source,
                Related = pair.Related,
            });

        // WHY: Matching has completed before owner payload is read. Direct only this hydration edge;
        // the source identity lookup remains an ordinary join and can keep its native access plan.
        if (!_capabilities.TryOrderedJoin(
                sourceNodes,
                _nodes,
                pair => pair.Related,
                key,
                (pair, related) => new AncestorIndexMappedPair
                {
                    Source = pair.Source,
                    Related = related,
                },
                out var pairs))
        {
            throw new NotSupportedException(
                "The bounded ancestor owner hydration requires the selected adapter's ordered-join primitive.");
        }

        // WHY: Exact mapped aliases retain their original visibility, identity and complete containment
        // after late payload hydration. The source binder shares selection across envelope and matching.
        pairs = WhereIndexedMappedAncestorContainment(
            pairs,
            pair => pair.Source,
            pair => pair.Related,
            includeSelf: false);

        var result = ProjectPairs(pairs, pair => pair.Related, pair => pair.Source, projection, ancestors: true);
        var completed = result;

        if (completeMatches)
        {
            var hydrate = Expression.Lambda<Func<IQueryable<AncestorIndexKeyPair>, IQueryable<TResult>>>(
                result.Expression,
                matchParameter);

            if (!_capabilities.TryBindSource(keys, hydrate, out completed))
            {
                return null;
            }
        }

        var compose = Expression.Lambda<Func<IQueryable<AncestorCoordinates>, IQueryable<TResult>>>(
            completed.Expression,
            sourceParameter);

        return _capabilities.TryBindSource(sources, compose, out var bound) ? bound : null;
    }

    /// <summary>Builds flat mapped structural roles without property-name closures or unmapped null columns.</summary>
    /// <returns>A context-free selector whose markers carry literal names from the exact finalized mapping.</returns>
    private Expression<Func<TEntity, AncestorCoordinates>> IndependentAncestorCoordinates()
    {
        var entity = Expression.Parameter(typeof(TEntity), "entity");
        var bindings = new List<MemberBinding>(5)
        {
            BindIndependentAncestorCoordinate(
                entity,
                nameof(AncestorCoordinates.Key),
                _descriptor.NodeKey,
                typeof(object)),
            BindIndependentAncestorCoordinate(
                entity,
                nameof(AncestorCoordinates.Tree),
                _descriptor.TreeId,
                typeof(object)),
            BindIndependentAncestorCoordinate(entity, nameof(AncestorCoordinates.Left), _descriptor.Left),
            BindIndependentAncestorCoordinate(entity, nameof(AncestorCoordinates.Right), _descriptor.Right),
        };

        // WHY: A scopeless model has no mapped partition column. Omitting this binding also avoids
        // introducing an object/null relational projection into the completed structural source.
        if (_descriptor.Scope is { } scope)
        {
            bindings.Add(
                BindIndependentAncestorCoordinate(entity, nameof(AncestorCoordinates.Scope), scope, typeof(object)));
        }

        return Expression.Lambda<Func<TEntity, AncestorCoordinates>>(
            Expression.MemberInit(Expression.New(typeof(AncestorCoordinates)), bindings),
            entity);
    }

    /// <summary>Uses native identity markers and exact numeric members through the shared mapped-access primitive.</summary>
    private MemberAssignment BindIndependentAncestorCoordinate(
        ParameterExpression entity,
        string member,
        NestedSetPropertyDescriptor descriptor,
        Type? markerType = null
    )
    {
        var property = descriptor.Resolve(_entityType);
        var value = markerType is not null
            ? NestedSetExpressions.Property(entity, property, markerType)
            : NestedSetExpressions.QueryProperty(entity, property);

        return Expression.Bind(typeof(AncestorCoordinates).GetProperty(member)!, value);
    }

    /// <summary>Preserves necessary independent interval limits for one nonempty selected tree.</summary>
    private sealed class AncestorEnvelope
    {
        /// <summary>Gets the native mapped partition point when the hierarchy configures Scope.</summary>
        public object? Scope { get; init; }

        /// <summary>Gets the native mapped tree identity used by both complete relations.</summary>
        public required object Tree { get; init; }

        /// <summary>Gets the greatest selected start that any matching strict ancestor must precede.</summary>
        public long MaxLeft { get; init; }

        /// <summary>Gets the least selected end that any matching strict ancestor must follow.</summary>
        public long MinRight { get; init; }
    }

    /// <summary>Retains one selected mapped source and the independent ancestor coordinates matched in SQL.</summary>
    private sealed class AncestorCoordinatePair
    {
        /// <summary>Gets the actual selected mapped source alias.</summary>
        public required TEntity Source { get; init; }

        /// <summary>Gets the independent ancestor coordinates matched against this source.</summary>
        public required AncestorCoordinates Coordinate { get; init; }
    }

    /// <summary>Restricts the independent nonleaf relation to identity and interval roles without payload.</summary>
    private sealed class AncestorCoordinates
    {
        /// <summary>Gets the ancestor's native mapped key without a CLR equality operator.</summary>
        public required object Key { get; init; }

        /// <summary>Gets the native mapped partition when candidate ranges use an unbound visible root.</summary>
        public object? Scope { get; init; }

        /// <summary>Gets the ancestor's mapped tree identity for forest joins.</summary>
        public required object Tree { get; init; }

        /// <summary>Gets the ancestor's maintained left coordinate.</summary>
        public long Left { get; init; }

        /// <summary>Gets the ancestor's maintained right coordinate.</summary>
        public long Right { get; init; }
    }
}

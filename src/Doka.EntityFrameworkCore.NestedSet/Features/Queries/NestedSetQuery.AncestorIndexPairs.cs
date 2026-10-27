using System.Diagnostics.CodeAnalysis;

namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

internal sealed partial class NestedSetQuery<TEntity>
{
    /// <summary>Uses the configured nonleaf index when the selected adapter supplies shared narrow selection.</summary>
    private bool TrySelectIndexedAncestorPairs<TResult>(
        Expression<Func<TEntity, bool>> selected,
        Expression<Func<TEntity, TEntity, int, TResult>> projection,
        bool includeSelf,
        [NotNullWhen(true)] out IQueryable<TResult>? query
    )
    {
        query = null;

        // WHY: A hierarchy-bearing predicate can correlate a later direction change with this selection.
        // Fencing that pivot before its outer filter expanded a million-node relation per origin.
        // Explicit independent fact roots may complete once; unknown sources retain the original path.
        // Inclusive matching additionally needs its ordered edge; source binding alone cannot
        // activate a candidate plan whose source modes may be visited after owner hydration.
        if (!_capabilities.UsesIndependentAncestorOwners
            || _descriptor.AncestorIndexProperty is not { } nonLeaf
            || NestedSetQuerySelection.RequiresAncestorDecorrelation(selected, _entityType.Model, out var factSelection)
            || _capabilities.CompletedSourceMethod is null
            || (includeSelf && _capabilities.OrderedJoinMethod is null))
        {
            return false;
        }

        var nonLeafName = nonLeaf.Name;
        var sources = AncestorIndexCoordinates(_nodes.Where(selected), includeNonLeaf: includeSelf);

        if (factSelection)
        {
            // WHY: These coordinates include the authoritative root's complete unique node identity.
            // DISTINCT preserves its rows while retaining an inner query block for fact selection;
            // otherwise MariaDB merges EXISTS into the recursive anchor and scans every source node.
            sources = sources.Distinct();
        }

        var sourceParameter = Expression.Parameter(typeof(IQueryable<IndexedAncestorCoordinates>), "selectedSources");
        var sharedSources = _nodes.Provider.CreateQuery<IndexedAncestorCoordinates>(sourceParameter);

        IQueryable<AncestorIndexMappedPair> pairs;

        if (includeSelf)
        {
            pairs = SelectIndexedInclusiveAncestorPairs(sharedSources, nonLeafName);
        }
        else
        {
            // WHY: Keep application visibility filters on this root, but do not copy the library's
            // constant Scope/Tree bindings. The completed selection supplies both points below;
            // duplicate constant prefixes suppress MariaDB's source-dependent range planning.
            var nonLeafOwners = AncestorIndexCoordinates(_unboundNodes, includeNonLeaf: true);
            var keys = SelectIndexedStrictAncestorKeys(sharedSources, nonLeafOwners);
            var keyName = _descriptor.NodeKey.Name;
            Expression<Func<TEntity, object>> key = entity => EF.Property<object>(entity, keyName);

            // WHY: The independent range relation carries structural values only. Rejoining mapped
            // entities retains native converters, navigation identity and arbitrary application payload.
            var sourceNodes = keys.Join(
                _nodes,
                pair => pair.Source,
                key,
                (pair, source) => new
                {
                    Source = source,
                    RelatedKey = pair.Related
                });

            pairs = sourceNodes.Join(
                _nodes,
                pair => pair.RelatedKey,
                key,
                (pair, related) => new AncestorIndexMappedPair
                {
                    Source = pair.Source,
                    Related = related
                });

            // WHY: Structural containment already rejects nonmatching candidates before hydration.
            // Actual aliases retain all four original bounds under the existing consistency boundary.
            pairs = WhereIndexedMappedAncestorContainment(
                pairs,
                pair => pair.Source,
                pair => pair.Related,
                includeSelf: false);
        }

        var result = ProjectPairs(pairs, pair => pair.Related, pair => pair.Source, projection, ancestors: true);
        var compose = Expression.Lambda<Func<IQueryable<IndexedAncestorCoordinates>, IQueryable<TResult>>>(
            result.Expression,
            sourceParameter);

        return _capabilities.TryBindSource(sources, compose, out query);
    }

    /// <summary>Projects the mapped identity and interval roles without introducing a payload boundary.</summary>
    private IQueryable<IndexedAncestorCoordinates> AncestorIndexCoordinates(
        IQueryable<TEntity> nodes,
        bool includeNonLeaf = false
    )
    {
        var projections = AncestorIndexCoordinateProjections.For(_entityType);

        return nodes.Select(includeNonLeaf ? projections.Candidate : projections.Source);
    }

    /// <summary>Prefilters independent owner intervals by a necessary envelope before matching any source.</summary>
    private IQueryable<IndexedAncestorCoordinates> SelectIndexedAncestorOwners(
        IQueryable<IndexedAncestorCoordinates> sources,
        IQueryable<IndexedAncestorCoordinates> owners
    ) => SelectIndexedOwnCoordinates(SelectIndexedAncestorOwnerCoordinates(sources, owners, includeSelf: false));

    /// <summary>Retains complete owner coordinates while applying the shared necessary source envelope.</summary>
    private IQueryable<IndexedAncestorCoordinates> SelectIndexedAncestorOwnerCoordinates(
        IQueryable<IndexedAncestorCoordinates> sources,
        IQueryable<IndexedAncestorCoordinates> owners,
        bool includeSelf
    )
    {
        var envelopes = _descriptor.Scope is null
            ? sources
                .GroupBy(source => source.Tree)
                .Select(group => new AncestorIndexEnvelope
                {
                    Tree = group.Key,
                    MaxLeft = group.Max(source => source.Left),
                    MinRight = group.Min(source => source.Right),
                    HasSources = group.LongCount() > 0,
                })
            : sources
                .GroupBy(source => new
                {
                    source.Scope,
                    source.Tree
                })
                .Select(group => new AncestorIndexEnvelope
                {
                    Scope = group.Key.Scope,
                    Tree = group.Key.Tree,
                    MaxLeft = group.Max(source => source.Left),
                    MinRight = group.Min(source => source.Right),
                    HasSources = group.LongCount() > 0,
                });

        Expression<Func<AncestorIndexEnvelope, IndexedAncestorCoordinates, bool>> prefix = (
                envelope,
                owner
            ) => NestedSetNativePointRange<object>.Contains(owner.Tree, envelope.Tree)
            && NestedSetNativePointRange<bool>.Contains(owner.NonLeaf, envelope.HasSources);

        Expression<Func<AncestorIndexEnvelope, IndexedAncestorCoordinates, bool>> bounds = includeSelf
            ? (envelope, owner) => owner.Left <= envelope.MaxLeft && owner.Right >= envelope.MinRight
            : (envelope, owner) => owner.Left < envelope.MaxLeft && owner.Right > envelope.MinRight;

        var envelope = Expression.Parameter(typeof(AncestorIndexEnvelope), "envelope");
        var owner = Expression.Parameter(typeof(IndexedAncestorCoordinates), "owner");
        var predicate = Expression.AndAlso(
            NestedSetQueryExpressions.Inline(prefix, envelope, owner),
            NestedSetQueryExpressions.Inline(bounds, envelope, owner));

        if (_descriptor.Scope is not null)
        {
            Expression<Func<AncestorIndexEnvelope, IndexedAncestorCoordinates, bool>> scope = (
                selected,
                candidate
            ) => NestedSetNativePointRange<object?>.Contains(candidate.Scope, selected.Scope);

            predicate = Expression.AndAlso(NestedSetQueryExpressions.Inline(scope, envelope, owner), predicate);
        }

        var candidates = envelopes.Join(
            owners,
            selected => true,
            candidate => true,
            (selected, candidate) => new AncestorIndexEnvelopeOwner
            {
                Envelope = selected,
                Owner = candidate,
            });

        var pair = Expression.Parameter(typeof(AncestorIndexEnvelopeOwner), "pair");
        var filter = Expression.Lambda<Func<AncestorIndexEnvelopeOwner, bool>>(
            NestedSetQueryExpressions.Inline(
                Expression.Lambda<Func<AncestorIndexEnvelope, IndexedAncestorCoordinates, bool>>(
                    predicate,
                    envelope,
                    owner),
                Expression.Property(pair, nameof(AncestorIndexEnvelopeOwner.Envelope)),
                Expression.Property(pair, nameof(AncestorIndexEnvelopeOwner.Owner))),
            pair);

        // WHY: Grouped nonempty sources supply dynamic points without sentinel values or a
        // second selection. Native closed intervals preserve column collation/converters while
        // allowing MariaDB to range-check each envelope against the existing compound index.
        // The complete original containment predicate still checks every resulting occurrence.
        return candidates
            .Where(filter)
            .Select(candidate => candidate.Owner);
    }

    /// <summary>Matches strict ancestors without introducing a set operation over the resulting pairs.</summary>
    private IQueryable<AncestorIndexKeyPair> SelectIndexedStrictAncestorKeys(
        IQueryable<IndexedAncestorCoordinates> sources,
        IQueryable<IndexedAncestorCoordinates> owners
    )
    {
        var candidates = SelectIndexedAncestorOwners(sources, owners);

        var ranges = sources.Join(
            candidates,
            source => source.Tree,
            related => related.Tree,
            (source, related) => new AncestorIndexStrictCoordinatePair
            {
                Source = source,
                Related = related
            });

        var pair = Expression.Parameter(typeof(AncestorIndexStrictCoordinatePair), "pair");
        var sourceCoordinates = Expression.Property(pair, nameof(AncestorIndexStrictCoordinatePair.Source));
        var ownerCoordinates = Expression.Property(pair, nameof(AncestorIndexStrictCoordinatePair.Related));
        var containment = ContainedBounds(
            Expression.Property(ownerCoordinates, nameof(IndexedAncestorCoordinates.Left)),
            Expression.Property(ownerCoordinates, nameof(IndexedAncestorCoordinates.Right)),
            Expression.Property(sourceCoordinates, nameof(IndexedAncestorCoordinates.Left)),
            Expression.Property(sourceCoordinates, nameof(IndexedAncestorCoordinates.Right)),
            includeSelf: false);

        // WHY: The necessary envelope can admit unrelated branches. Reject them on structural
        // coordinates before either mapped endpoint is hydrated; otherwise a broad selection
        // performs one primary-key lookup per rejected source/candidate combination.
        return ranges
            .Where(Expression.Lambda<Func<AncestorIndexStrictCoordinatePair, bool>>(containment, pair))
            .Select(match => new AncestorIndexKeyPair
            {
                Source = match.Source.Key,
                Related = match.Related.Key,
            });
    }

    /// <summary>
    /// Checks the complete original containment on exact mapped aliases after their existing identity joins.
    /// </summary>
    private IQueryable<TPair> WhereIndexedMappedAncestorContainment<TPair>(
        IQueryable<TPair> pairs,
        Expression<Func<TPair, TEntity>> source,
        Expression<Func<TPair, TEntity>> related,
        bool includeSelf
    )
    {
        var pair = Expression.Parameter(typeof(TPair), "pair");
        var containment = ContainedBounds(
            NestedSetQueryExpressions.Inline(related, pair),
            NestedSetQueryExpressions.Inline(source, pair),
            includeSelf);

        return pairs.Where(Expression.Lambda<Func<TPair, bool>>(containment, pair));
    }

    /// <summary>Retains only the mapped roles used by the optional indexed source and candidate relations.</summary>
    private sealed class IndexedAncestorCoordinates
    {
        /// <summary>Gets the exact scope-local mapped identity.</summary>
        public required object Key { get; init; }

        /// <summary>Gets the mapped tree point used by native range matching.</summary>
        public required object Tree { get; init; }

        /// <summary>Gets the mapped partition point when the hierarchy has an application Scope.</summary>
        public object? Scope { get; init; }

        /// <summary>Gets the maintained interval start.</summary>
        public long Left { get; init; }

        /// <summary>Gets the maintained interval end.</summary>
        public long Right { get; init; }

        /// <summary>Gets the generated candidate flag without including application payload.</summary>
        public bool NonLeaf { get; init; }
    }

    /// <summary>Bounds the completed selection independently for each physical partition and tree.</summary>
    private sealed class AncestorIndexEnvelope
    {
        /// <summary>Gets the mapped partition point when configured.</summary>
        public object? Scope { get; init; }

        /// <summary>Gets the mapped tree point.</summary>
        public required object Tree { get; init; }

        /// <summary>Gets the greatest selected interval start in this tree.</summary>
        public long MaxLeft { get; init; }

        /// <summary>Gets the least selected interval end in this tree.</summary>
        public long MinRight { get; init; }

        /// <summary>Gets a source-derived true point without a constant Boolean prefix.</summary>
        public bool HasSources { get; init; }
    }

    /// <summary>Retains independent relational aliases while applying native range predicates.</summary>
    private sealed class AncestorIndexEnvelopeOwner
    {
        /// <summary>Gets one completed source envelope.</summary>
        public required AncestorIndexEnvelope Envelope { get; init; }

        /// <summary>Gets a mapped candidate's structural coordinates.</summary>
        public required IndexedAncestorCoordinates Owner { get; init; }
    }

    /// <summary>Retains both finite structural intervals until strict containment rejects unrelated branches.</summary>
    private sealed class AncestorIndexStrictCoordinatePair
    {
        /// <summary>Gets one completed selected source's coordinates.</summary>
        public required IndexedAncestorCoordinates Source { get; init; }

        /// <summary>Gets the independent candidate's complete own coordinates.</summary>
        public required IndexedAncestorCoordinates Related { get; init; }
    }

    /// <summary>Carries exact structural identities into the existing late mapped entity joins.</summary>
    private sealed class AncestorIndexKeyPair
    {
        /// <summary>Gets the selected source's scope-local mapped identity.</summary>
        public required object Source { get; init; }

        /// <summary>Gets the related owner's scope-local mapped identity.</summary>
        public required object Related { get; init; }
    }

    /// <summary>Preserves actual mapped endpoints for payload, navigation and collection projections.</summary>
    private sealed class AncestorIndexMappedPair
    {
        /// <summary>Gets the actual selected mapped source.</summary>
        public required TEntity Source { get; init; }

        /// <summary>Gets the actual mapped ancestor.</summary>
        public required TEntity Related { get; init; }
    }
}

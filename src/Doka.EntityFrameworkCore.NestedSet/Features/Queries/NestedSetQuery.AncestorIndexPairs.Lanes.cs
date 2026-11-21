namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

internal sealed partial class NestedSetQuery<TEntity>
{
    /// <summary>
    /// Completes narrow owner candidates once before matching selected sources to actual mapped payload.
    /// </summary>
    private IQueryable<AncestorIndexMappedPair> SelectIndexedInclusiveAncestorPairs(
        IQueryable<IndexedAncestorCoordinates> sources,
        string nonLeafName
    )
    {
        var nonLeaf = SelectIndexedAncestorOwnerCoordinates(
            sources,
            AncestorIndexCoordinates(_unboundNodes, includeNonLeaf: true),
            includeSelf: true);

        var leaf = AncestorIndexCoordinates(_nodes.Where(owner => !EF.Property<bool>(owner, nonLeafName)))
            .Where(owner => owner.Right > owner.Left);

        // WHY: A false generated flag gives Right <= Left + 1. With the four original integral
        // containment comparisons, any matching positive-width leaf has exactly the source bounds.
        // Joining those coordinates selects only requested leaves rather than the leaf population.
        var leaves = _descriptor.Scope is null
            ? leaf.Join(
                sources,
                owner => new
                {
                    owner.Tree,
                    owner.Left,
                    owner.Right,
                },
                source => new
                {
                    source.Tree,
                    source.Left,
                    source.Right,
                },
                (owner, source) => owner)
            : leaf.Join(
                sources,
                owner => new
                {
                    owner.Scope,
                    owner.Tree,
                    owner.Left,
                    owner.Right,
                },
                source => new
                {
                    source.Scope,
                    source.Tree,
                    source.Left,
                    source.Right,
                },
                (owner, source) => owner);

        // WHY: Both joins contribute additional EF identifiers. Deduplicate the same five native
        // owner columns before adding mode constants, so UNION retains aligned nonempty identities
        // for future collection projections. Equal bounds never merge different physical node keys.
        var candidates = SelectIndexedMatchingCoordinates(SelectIndexedOwnCoordinates(nonLeaf), nonLeaf: true)
            .Union(SelectIndexedMatchingCoordinates(SelectIndexedOwnCoordinates(leaves), nonLeaf: false));

        // WHY: Under the four original comparisons, a false owner can match only a positive
        // width-one source. Its generated false flag plus positive width proves that condition
        // without signed addition, so nonleaf and malformed sources never enter the leaf mode.
        var sourceLanes = SelectIndexedMatchingCoordinates(sources, nonLeaf: true)
            .Union(
                SelectIndexedMatchingCoordinates(
                    sources.Where(source => !source.NonLeaf && source.Right > source.Left),
                    nonLeaf: false));

        var candidateParameter = Expression.Parameter(
            typeof(IQueryable<AncestorIndexMatchingCoordinates>),
            "candidateOwners");

        var sharedCandidates = _nodes.Provider.CreateQuery<AncestorIndexMatchingCoordinates>(candidateParameter);

        // WHY: Only this edge fixes source-mode availability before candidate lookup. Ordering
        // the full statement also constrains unrelated payload/consumer joins and regresses their plans.
        var matches = _descriptor.Scope is null
            ? JoinIndexedInclusiveCandidates(
                sourceLanes,
                sharedCandidates,
                source => new
                {
                    source.Tree,
                    source.NonLeaf,
                    source.MatchLeft,
                    source.MatchRight,
                },
                owner => new
                {
                    owner.Tree,
                    owner.NonLeaf,
                    owner.MatchLeft,
                    owner.MatchRight,
                })
            : JoinIndexedInclusiveCandidates(
                sourceLanes,
                sharedCandidates,
                source => new
                {
                    source.Scope,
                    source.Tree,
                    source.NonLeaf,
                    source.MatchLeft,
                    source.MatchRight,
                },
                owner => new
                {
                    owner.Scope,
                    owner.Tree,
                    owner.NonLeaf,
                    owner.MatchLeft,
                    owner.MatchRight,
                });

        // WHY: Rejecting temporary structural candidates before mapped payload hydration avoids
        // one primary-key lookup for every nonmatching source/candidate combination in a forest.
        // The final actual aliases retain the complete original comparisons as well.
        matches = matches.Where(pair => pair.Related.Left <= pair.Source.Left
            && pair.Related.Right >= pair.Source.Right
            && pair.Source.Left < pair.Related.Right
            && pair.Source.Right > pair.Related.Left);

        var keyName = _descriptor.NodeKey.Name;
        var treeName = _descriptor.TreeId.Name;
        var scopeName = _descriptor.Scope?.Name;

        // WHY: Both actual endpoint aliases retain every application filter. Each hydration join
        // uses that endpoint's own complete native identity, without source-owned Scope shortcuts
        // or an entity/collection source boundary. Only structural candidates are materialized.
        var selected = scopeName is null
            ? matches.Join(
                _unboundNodes,
                match => new
                {
                    match.Source.Key,
                    match.Source.Tree,
                },
                node => new
                {
                    Key = EF.Property<object>(node, keyName),
                    Tree = EF.Property<object>(node, treeName),
                },
                (match, node) => new AncestorIndexHydratedSource
                {
                    Related = match.Related,
                    Source = node,
                })
            : matches.Join(
                _unboundNodes,
                match => new
                {
                    match.Source.Key,
                    match.Source.Scope,
                    match.Source.Tree,
                },
                node => new
                {
                    Key = EF.Property<object>(node, keyName),
                    Scope = (object?)EF.Property<object>(node, scopeName),
                    Tree = EF.Property<object>(node, treeName),
                },
                (match, node) => new AncestorIndexHydratedSource
                {
                    Related = match.Related,
                    Source = node,
                });

        var result = scopeName is null
            ? JoinIndexedInclusiveOwners(
                selected,
                match => new
                {
                    match.Related.Key,
                    match.Related.Tree,
                },
                node => new
                {
                    Key = EF.Property<object>(node, keyName),
                    Tree = EF.Property<object>(node, treeName),
                })
            : JoinIndexedInclusiveOwners(
                selected,
                match => new
                {
                    match.Related.Key,
                    match.Related.Scope,
                    match.Related.Tree,
                },
                node => new
                {
                    Key = EF.Property<object>(node, keyName),
                    Scope = (object?)EF.Property<object>(node, scopeName),
                    Tree = EF.Property<object>(node, treeName),
                });

        // WHY: The actual mapped aliases keep the original public containment contract under
        // the query's existing database consistency boundary, after structural prefiltering.
        result = WhereIndexedMappedAncestorContainment(
            result,
            pair => pair.Source,
            pair => pair.Related,
            includeSelf: true);

        var compose =
            Expression.Lambda<Func<IQueryable<AncestorIndexMatchingCoordinates>, IQueryable<AncestorIndexMappedPair>>>(
                result.Expression,
                candidateParameter);

        // WHY: The outer selected-source binding has already verified this adapter primitive.
        // The dependent candidate definition refers to that lexical source, so it is completed
        // once before pair fanout rather than fencing an output-sized population or actual payload.
        if (!_capabilities.TryBindSource(candidates, compose, out var bound))
        {
            throw new NotSupportedException(
                "The indexed ancestor candidate requires the selected adapter's completed-source primitive.");
        }

        return bound;
    }

    /// <summary>
    /// Orders only the source-mode to completed-candidate edge through the verified public provider API.
    /// </summary>
    private IQueryable<AncestorIndexCoordinatePair> JoinIndexedInclusiveCandidates<TKey>(
        IQueryable<AncestorIndexMatchingCoordinates> sources,
        IQueryable<AncestorIndexMatchingCoordinates> candidates,
        Expression<Func<AncestorIndexMatchingCoordinates, TKey>> sourceKey,
        Expression<Func<AncestorIndexMatchingCoordinates, TKey>> candidateKey
    )
    {
        // WHY: Inclusive activation already validated this primitive. An unavailable ordered edge
        // must never silently execute the known unqualified ordinary-join strategy.
        if (!_capabilities.TryOrderedJoin(
            sources,
            candidates,
            sourceKey,
            candidateKey,
            (source, owner) => new AncestorIndexCoordinatePair
            {
                Source = source,
                Related = owner,
            },
            out var joined))
        {
            throw new NotSupportedException(
                "The indexed inclusive ancestor candidate requires the selected adapter's ordered-join primitive.");
        }

        return joined;
    }

    /// <summary>Hydrates each matched owner only after its structural candidate is available.</summary>
    private IQueryable<AncestorIndexMappedPair> JoinIndexedInclusiveOwners<TKey>(
        IQueryable<AncestorIndexHydratedSource> selected,
        Expression<Func<AncestorIndexHydratedSource, TKey>> matchKey,
        Expression<Func<TEntity, TKey>> ownerKey
    )
    {
        // WHY: MySQL can move owner payload before candidate matching and repeat scope-only table
        // scans. Direct only this final dependency; ordinary source hydration must retain its cheaper
        // selected-source lookups instead of being forced to run once for every resulting pair.
        if (!_capabilities.TryOrderedJoin(
            selected,
            _unboundNodes,
            matchKey,
            ownerKey,
            (match, owner) => new AncestorIndexMappedPair
            {
                Source = match.Source,
                Related = owner,
            },
            out var joined))
        {
            throw new NotSupportedException(
                "The indexed inclusive owner hydration requires the selected adapter's ordered-join primitive.");
        }

        return joined;
    }

    /// <summary>Aligns exact own coordinate identifiers before adding disjoint matching expressions.</summary>
    private IQueryable<IndexedAncestorCoordinates> SelectIndexedOwnCoordinates(
        IQueryable<IndexedAncestorCoordinates> coordinates
    ) => coordinates
        .Select(
            _descriptor.Scope is null
                ? AncestorIndexMatchingProjections.Own
                : AncestorIndexMatchingProjections.ScopedOwn)
        .Distinct();

    /// <summary>Retains own identity and the disjoint mode's native matching coordinates.</summary>
    private IQueryable<AncestorIndexMatchingCoordinates> SelectIndexedMatchingCoordinates(
        IQueryable<IndexedAncestorCoordinates> coordinates,
        bool nonLeaf
    ) => coordinates.Select(
        _descriptor.Scope is null
            ? nonLeaf
                ? AncestorIndexMatchingProjections.NonLeaf
                : AncestorIndexMatchingProjections.Leaf
            : nonLeaf
                ? AncestorIndexMatchingProjections.ScopedNonLeaf
                : AncestorIndexMatchingProjections.ScopedLeaf);
}

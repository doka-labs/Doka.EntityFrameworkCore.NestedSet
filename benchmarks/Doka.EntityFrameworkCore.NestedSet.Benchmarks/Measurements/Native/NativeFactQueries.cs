using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Builds the exact deferred captured-fact and occurrence-bag shapes used by native work contracts.</summary>
internal static class NativeFactQueries
{
    internal static readonly Guid Scope = Guid.Parse("61616161-6161-6161-6161-616161616161");
    internal static readonly Guid Tree = Guid.Parse("62626262-6262-6262-6262-626262626262");

    /// <summary>Constructs the complete captured selection and preserves duplicate consumer occurrences.</summary>
    internal static IQueryable<NativeFactPair> Build(
        NativeIndexedContext context,
        NativeFactCase scenario,
        int[] selected
    )
    {
        var scope = Scope;
        var tree = Tree;
        var label = scenario.Matches ? "selected" : "missing";
        var facts = context.Set<NativeIndexedFact>().AsNoTracking()
            .Where(fact => fact.Scope == scope && fact.Label == label);
        Expression<Func<NativeIndexedNode, bool>> selection = source => facts.Any(fact => fact.NodeId == source.Id);

        if (scenario.Operation == "Union")
        {
            var first = context.Set<NativeIndexedFact>().AsNoTracking()
                .Where(fact => fact.Scope == scope && fact.Label == "selected")
                .Select(fact => new FactKey { Node = fact.NodeId });
            var second = context.Set<NativeIndexedFact>().AsNoTracking()
                .Where(fact => fact.Scope == scope && fact.Label == "occurrence")
                .Select(fact => new FactKey { Node = fact.NodeId });
            var third = context.Set<NativeIndexedFact>().AsNoTracking()
                .Where(fact => fact.Scope == scope && fact.Label == "union-extra")
                .Select(fact => new FactKey { Node = fact.NodeId });
            var union = first.Concat(second).Concat(third);
            selection = source => union.Any(fact => fact.Node == source.Id);
        }
        else if (scenario.Operation is "Any" or "Contains" or "NotAny")
        {
            selection = source => Enumerable.Contains(selected, source.Id);
        }

        var hierarchy = context.NestedSet<NativeIndexedNode>().ForScope(scope).InTree(tree);
        var pairs = scenario.Inclusive
            ? hierarchy.SelectAncestorsOrSelf(selection, (source, owner, distance) => new HierarchyPair
            {
                Source = source.Id,
                Owner = owner.Id,
                Distance = distance,
            })
            : hierarchy.SelectAncestors(selection, (source, owner, distance) => new HierarchyPair
            {
                Source = source.Id,
                Owner = owner.Id,
                Distance = distance,
            });

        if (scenario.Operation is "Any" or "Contains" or "NotAny")
        {
            var ownerLabel = scenario.Matches ? "owner" : "missing";
            var owners = context.Set<NativeIndexedFact>().AsNoTracking()
                .Where(fact => fact.Scope == scope && fact.Label == ownerLabel);
            pairs = scenario.Operation switch
            {
                "Any" => pairs.Where(pair => owners.Any(owner => owner.NodeId == pair.Owner)),
                "Contains" => pairs.Where(pair => owners.Select(owner => owner.NodeId).Contains(pair.Owner)),
                "NotAny" => pairs.Where(pair => !owners.Any(owner => owner.NodeId == pair.Owner)),
                _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
            };
        }

        return pairs.Join(context.Set<NativeIndexedFact>().AsNoTracking()
                .Where(fact => fact.Scope == scope && fact.Label == "occurrence"),
            pair => pair.Source, fact => fact.NodeId,
            (pair, fact) => new NativeFactPair(fact.Id, pair.Source, pair.Owner, pair.Distance));
    }

    /// <summary>
    /// Reproduces the independent source identity plans of the star, chain and million-node controls.
    /// </summary>
    internal static int[] Selected(NativeFactCase scenario) => scenario.Nodes == 1_000_000
        ? Enumerable.Range(2, 1_000).ToArray()
        : scenario.Chain ? [1_001, 1_002] : [20, 30];

    /// <summary>Supplies a union-compatible projection without introducing a client materialization.</summary>
    private sealed record FactKey
    {
        /// <summary>Gets the projected fact's hierarchy key.</summary>
        public int Node { get; init; }
    }

    /// <summary>Retains both hierarchy endpoint identities for subsequent consumer semijoins.</summary>
    private sealed record HierarchyPair
    {
        /// <summary>Gets the selected hierarchy source.</summary>
        public int Source { get; init; }

        /// <summary>Gets the selected structural ancestor.</summary>
        public int Owner { get; init; }

        /// <summary>Gets the actual depth distance between source and ancestor.</summary>
        public int Distance { get; init; }
    }
}

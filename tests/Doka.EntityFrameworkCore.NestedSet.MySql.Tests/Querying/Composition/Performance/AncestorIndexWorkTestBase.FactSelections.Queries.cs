using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

public abstract partial class AncestorIndexWorkTestBase
{
    /// <summary>Selects the source with ordinary captured fact LINQ that EF normalizes at execution.</summary>
    private static Expression<Func<CompositionNavigationNode, bool>> FactSelection(
        AncestorIndexQueryContext context,
        Guid scope,
        string label
    )
    {
        var facts = context
            .Set<CompositionNavigationItem>()
            .AsNoTracking()
            .Where(fact => fact.Scope == scope && fact.Label == label);

        // WHY: Native work evidence must cover ordinary consumer LINQ, including EF's deferred
        // capture expansion, rather than a manually embedded root that bypasses that public spelling.
        return source => facts.Any(fact => fact.NodeId == source.Id);
    }

    /// <summary>Selects through three captured projected fact roots using the sample's UNION ALL shape.</summary>
    private static Expression<Func<CompositionNavigationNode, bool>> UnionFactSelection(
        AncestorIndexQueryContext context,
        Guid scope
    )
    {
        var selected = context
            .Set<CompositionNavigationItem>()
            .AsNoTracking()
            .Where(fact => fact.Scope == scope && fact.Label == "selected")
            .Select(fact => new ConsumerFactKey { Node = fact.NodeId });

        var occurrences = context
            .Set<CompositionNavigationItem>()
            .AsNoTracking()
            .Where(fact => fact.Scope == scope && fact.Label == "occurrence")
            .Select(fact => new ConsumerFactKey { Node = fact.NodeId });

        var extra = context
            .Set<CompositionNavigationItem>()
            .AsNoTracking()
            .Where(fact => fact.Scope == scope && fact.Label == "union-extra")
            .Select(fact => new ConsumerFactKey { Node = fact.NodeId });

        var facts = selected
            .Concat(occurrences)
            .Concat(extra);

        // WHY: UNION ALL retains duplicate facts, while Any selects each hierarchy source once.
        // The unchanged work oracle now also guards EF-normalized projected captures.
        return source => facts.Any(fact => fact.Node == source.Id);
    }

    /// <summary>Retains independent application occurrences after the hierarchy has selected source nodes.</summary>
    private static IQueryable<ConsumerFactPair> WithConsumerOccurrences(
        AncestorIndexQueryContext context,
        Guid scope,
        IQueryable<ConsumerHierarchyPair> pairs
    ) => pairs.Join(
        context
            .Set<CompositionNavigationItem>()
            .AsNoTracking()
            .Where(fact => fact.Scope == scope && fact.Label == "occurrence"),
        pair => pair.Source,
        fact => fact.NodeId,
        (pair, fact) => new ConsumerFactPair(fact.Id, pair.Source, pair.Owner, pair.Distance));

    /// <summary>Fails immediately when a timed read changes identity, multiplicity, distance or completeness.</summary>
    private static void RequireConsumerOracle(
        ConsumerFactPair[] actual,
        ConsumerFactPair[] expected
    )
    {
        if (!OrderConsumerPairs(actual)
            .SequenceEqual(expected))
        {
            throw new InvalidOperationException("The consumer occurrence bag differs from the adjacency oracle.");
        }
    }

    /// <summary>Separates warmup statements from the fixed complete-read sample window.</summary>
    private static void ClearConsumerCapture(
        CompositionCommandProbe commands,
        EnterpriseProbe capture
    )
    {
        commands.Reset();
        capture.Commands.Clear();
        capture.ParameterValues.Clear();
        capture.ParameterNames.Clear();
        capture.ParameterBindings.Clear();
    }
}

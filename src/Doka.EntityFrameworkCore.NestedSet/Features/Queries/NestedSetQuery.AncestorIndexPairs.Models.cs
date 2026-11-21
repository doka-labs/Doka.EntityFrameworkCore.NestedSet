namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

internal sealed partial class NestedSetQuery<TEntity>
{
    /// <summary>Defers reusable structural projections until this optional indexed strategy is selected.</summary>
    private static class AncestorIndexMatchingProjections
    {
        /// <summary>Retains the same five native columns in both candidate arms before DISTINCT.</summary>
        internal static readonly Expression<Func<IndexedAncestorCoordinates, IndexedAncestorCoordinates>> ScopedOwn =
            owner => new IndexedAncestorCoordinates
            {
                Key = owner.Key,
                Scope = owner.Scope,
                Tree = owner.Tree,
                Left = owner.Left,
                Right = owner.Right,
            };

        /// <summary>Omits unconfigured Scope while aligning the native unscoped candidate columns.</summary>
        internal static readonly Expression<Func<IndexedAncestorCoordinates, IndexedAncestorCoordinates>> Own = owner =>
            new IndexedAncestorCoordinates
            {
                Key = owner.Key,
                Tree = owner.Tree,
                Left = owner.Left,
                Right = owner.Right,
            };

        // WHY: MySQL builds temporary key parts in projected table-column order. Equality mode
        // and mask columns precede range coordinates so the automatic index can use that prefix.
        /// <summary>Masks nonleaf matching coordinates while retaining exact unscoped identity.</summary>
        internal static readonly Expression<Func<IndexedAncestorCoordinates, AncestorIndexMatchingCoordinates>>
            NonLeaf = owner => new AncestorIndexMatchingCoordinates
            {
                Key = owner.Key,
                Tree = owner.Tree,
                NonLeaf = true,
                MatchLeft = 0,
                MatchRight = 0,
                Left = owner.Left,
                Right = owner.Right,
            };

        /// <summary>Matches unscoped leaf bounds exactly while retaining exact owner identity.</summary>
        internal static readonly Expression<Func<IndexedAncestorCoordinates, AncestorIndexMatchingCoordinates>> Leaf =
            owner => new AncestorIndexMatchingCoordinates
            {
                Key = owner.Key,
                Tree = owner.Tree,
                NonLeaf = false,
                MatchLeft = owner.Left,
                MatchRight = owner.Right,
                Left = owner.Left,
                Right = owner.Right,
            };

        /// <summary>Retains scoped native identity with masked nonleaf matching coordinates.</summary>
        internal static readonly Expression<Func<IndexedAncestorCoordinates, AncestorIndexMatchingCoordinates>>
            ScopedNonLeaf = owner => new AncestorIndexMatchingCoordinates
            {
                Key = owner.Key,
                Scope = owner.Scope,
                Tree = owner.Tree,
                NonLeaf = true,
                MatchLeft = 0,
                MatchRight = 0,
                Left = owner.Left,
                Right = owner.Right,
            };

        /// <summary>Retains scoped native identity and exact leaf matching coordinates.</summary>
        internal static readonly Expression<Func<IndexedAncestorCoordinates, AncestorIndexMatchingCoordinates>>
            ScopedLeaf = owner => new AncestorIndexMatchingCoordinates
            {
                Key = owner.Key,
                Scope = owner.Scope,
                Tree = owner.Tree,
                NonLeaf = false,
                MatchLeft = owner.Left,
                MatchRight = owner.Right,
                Left = owner.Left,
                Right = owner.Right,
            };
    }

    /// <summary>Carries complete own identity and disjoint matching keys through the bounded candidate fence.</summary>
    private sealed class AncestorIndexMatchingCoordinates
    {
        /// <summary>Gets the exact scope-local mapped node identity.</summary>
        public required object Key { get; init; }

        /// <summary>Gets the mapped partition identity when configured.</summary>
        public object? Scope { get; init; }

        /// <summary>Gets the mapped tree identity.</summary>
        public required object Tree { get; init; }

        /// <summary>Gets the disjoint owner-flag matching mode.</summary>
        public bool NonLeaf { get; init; }

        /// <summary>Gets the exact leaf start or mode-separated nonleaf zero mask.</summary>
        public long MatchLeft { get; init; }

        /// <summary>Gets the exact leaf end or mode-separated nonleaf zero mask.</summary>
        public long MatchRight { get; init; }

        /// <summary>Gets the actual completed interval start used before payload hydration.</summary>
        public long Left { get; init; }

        /// <summary>Gets the actual completed interval end used before payload hydration.</summary>
        public long Right { get; init; }
    }

    /// <summary>Matches completed structural sources and candidates before ordinary mapped hydration.</summary>
    private sealed class AncestorIndexCoordinatePair
    {
        /// <summary>Gets one completed selected source.</summary>
        public required AncestorIndexMatchingCoordinates Source { get; init; }

        /// <summary>Gets one completed candidate's own identity and matching keys.</summary>
        public required AncestorIndexMatchingCoordinates Related { get; init; }
    }

    /// <summary>Retains candidate-owned identity while rehydrating the selected mapped source.</summary>
    private sealed class AncestorIndexHydratedSource
    {
        /// <summary>Gets the exact completed candidate owner coordinates.</summary>
        public required AncestorIndexMatchingCoordinates Related { get; init; }

        /// <summary>Gets the actual visible mapped source.</summary>
        public required TEntity Source { get; init; }
    }
}

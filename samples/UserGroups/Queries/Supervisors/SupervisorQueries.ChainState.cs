namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

internal static partial class SupervisorQueries
{
    /// <summary>Deduplicates only identical current searches; step rules are shared within one frontier.</summary>
    private readonly record struct ChainSearchKey(
        int User,
        int? Group,
        int? RoutingUser,
        int? RoutingGroup
    );

    /// <summary>Shares immutable path prefixes while keeping cycle detection local to each provenance path.</summary>
    internal sealed record ChainPath(
        SupervisorHop Hop,
        ChainPath? Previous,
        int InitialGroupId,
        Guid InitialTreeId
    )
    {
        /// <summary>Finds an already visited person on this path, never on unrelated convergent paths.</summary>
        internal bool Contains(
            int person
        )
        {
            for (var node = this; node is not null; node = node.Previous)
            {
                if (node.Hop.Evidence.CandidateUserId == person)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Materializes only the selected successful hops in their forward order.</summary>
        internal IReadOnlyList<SupervisorHop> Materialize()
        {
            var rows = new SupervisorHop[Hop.Number];
            for (var node = this; node is not null; node = node.Previous)
            {
                rows[node.Hop.Number - 1] = node.Hop;
            }

            return rows;
        }
    }

    /// <summary>
    /// Checks physical reads and logical allocations independently before either can exceed the request.
    /// </summary>
    internal sealed class ChainBudget(SupervisorChainRequest request)
    {
        private int _reads;
        private int _visits;
        private int _results;

        /// <summary>Gets the explicit consumer limits.</summary>
        internal SupervisorChainRequest Request { get; } = request;

        /// <summary>Gets the remaining physical rows before the next reader's SQL overflow probe.</summary>
        internal int RemainingReads => Request.MaxResultRows - _reads;

        /// <summary>Admits one physical status/evidence row before buffering.</summary>
        internal bool Read()
        {
            if (_reads >= Request.MaxResultRows)
            {
                return false;
            }

            _reads++;

            return true;
        }

        /// <summary>Admits one new retained person-path node before allocation.</summary>
        internal bool Visit()
        {
            if (_visits >= Request.MaxVisitedStates)
            {
                return false;
            }

            _visits++;

            return true;
        }

        /// <summary>Admits retained logical rows before allocating a hop, status, or complete path copy.</summary>
        /// <param name="count">All copied hop rows plus their terminal status, or one newly shared hop node.</param>
        /// <returns>Whether the complete allocation fits the remaining result-row budget.</returns>
        internal bool Result(
            int count = 1
        )
        {
            if (count < 1
                || count > Request.MaxResultRows - _results)
            {
                return false;
            }

            _results += count;

            return true;
        }
    }
}

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

public abstract partial class AncestorIndexWorkTestBase
{
    /// <summary>Retains stable consumer occurrence identity separately from hierarchy endpoints.</summary>
    private sealed record ConsumerFactPair(
        int Occurrence,
        int Source,
        int Owner,
        int Distance
    );

    /// <summary>Keeps SQL-composable hierarchy projection members available to consumer predicates.</summary>
    private sealed record ConsumerHierarchyPair
    {
        /// <summary>Gets the selected hierarchy source identity.</summary>
        public int Source { get; init; }

        /// <summary>Gets the matching ancestor identity.</summary>
        public int Owner { get; init; }

        /// <summary>Gets the structural distance from source to ancestor.</summary>
        public int Distance { get; init; }
    }

    /// <summary>Projects independent fact sources to one union-compatible node key.</summary>
    private sealed record ConsumerFactKey
    {
        /// <summary>Gets the hierarchy node referenced by the fact.</summary>
        public int Node { get; init; }
    }

    /// <summary>Retains one complete asynchronous read's elapsed time and process-wide managed allocation.</summary>
    private sealed record ConsumerReadSample(
        double Milliseconds,
        long ProcessAllocatedBytes
    );

    /// <summary>Retains deterministic observations until the test's single assertion phase.</summary>
    private sealed record ConsumerFactObservation(
        string Profile,
        ConsumerFactPair[] Actual,
        ConsumerFactPair[] Expected,
        bool BoundedPathRequired,
        long ConstructionBytes,
        int ConstructionCommands,
        int Commands,
        long Traversals,
        long IndexAttempts,
        DensePathNativeWork Work,
        int Tracked,
        int Population,
        int Reads
    );
}

namespace Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

internal sealed partial class NestedSetSaveGroup<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    /// <summary>Projects immutable identifiers without creating or tracking a hierarchy entity.</summary>
    /// <param name="Key">The persisted primary key returned using its provider representation.</param>
    /// <param name="Scope">The persisted scope value whose comparison belongs to the database.</param>
    /// <param name="TreeId">The persisted tree identity whose comparison belongs to the database.</param>
    private sealed record PersistedIdentity(
        TKey Key,
        TScope Scope,
        TTreeId TreeId
    );

    /// <summary>Projects only the locked identity and coordinates needed for move dependency planning.</summary>
    private sealed record ParentEndpoint(
        int Ordinal,
        TTreeId TreeId,
        long Left,
        long Right
    );

    /// <summary>Pairs one requested change with its persisted source and destination endpoints.</summary>
    private sealed record ParentMovePlan(
        ParentChange Change,
        TScope Scope,
        ParentEndpoint Source,
        ParentEndpoint Target
    );

    /// <summary>Indexes move endpoints once per affected tree for bounded dependency sweeps.</summary>
    private sealed class ParentTreePlan
    {
        internal List<(ParentMovePlan plan, int index)> Sources { get; } = [];

        internal List<(ParentMovePlan plan, int index)> Targets { get; } = [];
    }

    /// <summary>Retains one requested Parent change while the payload save uses the persisted Parent.</summary>
    private sealed class ParentChange
    {
        internal ParentChange(
            EntityEntry<TEntity> entry,
            TKey key,
            object? originalParent,
            TKey targetParent,
            TScope sourceScope
        )
        {
            Entry = entry;
            Key = key;
            OriginalParent = originalParent;
            TargetParent = targetParent;
            SourceScope = sourceScope;
        }

        internal EntityEntry<TEntity> Entry { get; }

        internal TKey Key { get; }

        // WHY: Original parent state belongs to EF rollback metadata and can be null or a configured sentinel.
        // It is preserved exactly, rather than treated as an active non-null typed parent identity.
        internal object? OriginalParent { get; }

        internal TKey TargetParent { get; }

        internal TScope SourceScope { get; }
    }

    /// <summary>Correlates a requested Parent ordinal with its persisted target tree.</summary>
    private sealed record TargetIdentity(
        int Ordinal,
        TTreeId TreeId
    );

    /// <summary>Identifies one strict sibling group after any Parent moves have completed.</summary>
    private sealed record ReorderIdentity(
        TTreeId TreeId,
        NestedSetParent<TKey> Parent
    );

    /// <summary>Identifies one changed node placed individually under an order that allows manual placement.</summary>
    private sealed record PlacedIdentity(
        TKey Key,
        TTreeId TreeId
    );

    /// <summary>Collects one tree's changed keys and records whether key batches split their rule order.</summary>
    private sealed class PlacedTree
    {
        private readonly int _firstBatch;

        /// <summary>Starts the tree with the key batch that first returned one of its nodes.</summary>
        internal PlacedTree(
            int firstBatch
        )
        {
            _firstBatch = firstBatch;
        }

        /// <summary>Gets the changed keys in rule order within each contributing batch.</summary>
        internal List<TKey> Keys { get; } = [];

        /// <summary>Gets whether several batches contributed keys, so one database order must be restored.</summary>
        internal bool SpansBatches { get; private set; }

        /// <summary>Appends a key returned by the given ordered batch.</summary>
        internal void Add(
            TKey key,
            int batch
        )
        {
            Keys.Add(key);
            SpansBatches |= batch != _firstBatch;
        }
    }

    /// <summary>Accumulates changed coordinate spans for one exact persisted tree identity.</summary>
    private sealed class TreeIntervals
    {
        internal TreeIntervals(
            TScope scope,
            TTreeId treeId
        )
        {
            Scope = scope;
            TreeId = treeId;
        }

        internal TScope Scope { get; }

        internal TTreeId TreeId { get; }

        internal List<NestedSetChangedInterval> Intervals { get; } = [];
    }
}

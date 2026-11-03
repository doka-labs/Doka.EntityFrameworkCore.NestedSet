namespace Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

internal static partial class NestedSetSaveChanges
{
    // WHY: Immutable table metadata is model-owned. Caching the relevant array weakly avoids the measured
    // 40-byte interface enumerator on every ordinary callback write without retaining unreachable models.
    private static readonly ConditionalWeakTable<IEntityType, ITable[]> s_callbackSharingTables = new();

    /// <summary>Includes tracked peers that EF can promote while preparing the callback's shared rows.</summary>
    /// <param name="context">The context whose existing public update adapter resolves row-sharing identities.</param>
    /// <param name="callbackWrites">The private callback snapshot list, distinct from EF's actual write set.</param>
    internal static void IncludeManagedSharingPeers(
        DbContext context,
        List<IUpdateEntry> callbackWrites
    )
    {
        IUpdateAdapter? updates = null;
        HashSet<IUpdateEntry>? captured = null;
        Dictionary<ITable, HashSet<IUpdateEntry>>? visited = null;
        Stack<IUpdateEntry>? pending = null;
        var seedCount = callbackWrites.Count;

        // WHY: EF adds sharing peers after command creation. Only original callback writes seed physical
        // tables; following newly found peers' other mappings would capture unrelated rows and state.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < seedCount; index++)
        {
            var source = callbackWrites[index];

            foreach (var table in s_callbackSharingTables.GetValue(
                         source.EntityType,
                         static type => type
                             .GetTableMappings()
                             .Select(mapping => mapping.Table)
                             .Where(table => table.IsShared
                                 && (table.GetRowInternalForeignKeys(type).Any()
                                     || table.GetReferencingRowInternalForeignKeys(type).Any()))
                             .ToArray()))
            {
                updates ??= context
                    .GetService<IUpdateAdapterFactory>()
                    .Create();

                captured ??= new HashSet<IUpdateEntry>(callbackWrites, ReferenceEqualityComparer.Instance);
                visited ??= new Dictionary<ITable, HashSet<IUpdateEntry>>(ReferenceEqualityComparer.Instance);
                pending ??= new Stack<IUpdateEntry>();

                if (!visited.TryGetValue(table, out var rowEntries))
                {
                    rowEntries = new HashSet<IUpdateEntry>(ReferenceEqualityComparer.Instance);
                    visited.Add(table, rowEntries);
                }

                if (rowEntries.Contains(source))
                {
                    continue;
                }

                // WHY: Match EF's same-row command walker: ascend to its first tracked main entry,
                // then descend that entry's exact metadata. A base-type walk would invent extra promotions.
                var main = source;

                while (true)
                {
                    IUpdateEntry? principal = null;

                    foreach (var key in table.GetRowInternalForeignKeys(main.EntityType))
                    {
                        principal = updates.FindPrincipal(main, key);

                        if (principal is not null)
                        {
                            break;
                        }
                    }

                    if (principal is null)
                    {
                        break;
                    }

                    main = principal;
                }

                pending.Push(main);

                while (pending.TryPop(out var entry))
                {
                    if (!rowEntries.Add(entry))
                    {
                        continue;
                    }

                    // WHY: Modified, Added and Deleted callbacks are already captured; planned Added
                    // nodes retain insertion ownership. EF never promotes unchanged JSON-owned entries.
                    if (entry.EntityState == EntityState.Unchanged
                        && !entry.EntityType.IsMappedToJson()
                        && captured.Add(entry))
                    {
                        callbackWrites.Add(entry);
                    }

                    // WHY: Exact same-table row metadata handles owned and ordinary table splitting,
                    // including reverse/nested aliases, without treating normal FK relations as sharing.
                    foreach (var key in table.GetReferencingRowInternalForeignKeys(entry.EntityType))
                    {
                        foreach (var dependent in updates.GetDependents(entry, key))
                        {
                            pending.Push(dependent);
                        }
                    }
                }
            }
        }
    }
}

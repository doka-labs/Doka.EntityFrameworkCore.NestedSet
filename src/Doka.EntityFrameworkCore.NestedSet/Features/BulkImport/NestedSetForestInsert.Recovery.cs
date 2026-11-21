namespace Doka.EntityFrameworkCore.NestedSet.Features.BulkImport;

internal sealed partial class NestedSetForestInsert<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    /// <summary>Restores every input after rollback and preserves both operation and cleanup failures.</summary>
    private static void Restore(
        IReadOnlyList<PreparedTree> prepared,
        NestedSetCallbackWrites callbackWrites,
        Exception operationError
    )
    {
        List<Exception>? errors = null;

        // WHY: Index this stable list directly without creating an enumerator or a copied buffer.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (int treeIndex = 0, treeCount = prepared.Count; treeIndex < treeCount; treeIndex++)
        {
            var tree = prepared[treeIndex];

            try
            {
                tree.Plan.Restore();
            }
            catch (Exception restoreError)
            {
                (errors ??= []).Add(restoreError);
            }
        }

        try
        {
            callbackWrites.Restore();
        }
        catch (Exception restoreError)
        {
            (errors ??= []).Add(restoreError);
        }

        if (errors is not null)
        {
            throw new AggregateException(
                "Nested-set forest restoration failed. Discard the context.",
                [operationError, .. errors]);
        }
    }

    /// <summary>Ensures every successfully imported input remains detached from the caller's context.</summary>
    private static void Detach(
        IReadOnlyList<PreparedTree> prepared
    )
    {
        List<Exception>? errors = null;

        // WHY: Index this stable list directly without creating an enumerator or a copied buffer.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (int treeIndex2 = 0, treeCount2 = prepared.Count; treeIndex2 < treeCount2; treeIndex2++)
        {
            var tree = prepared[treeIndex2];

            try
            {
                tree.Plan.Detach();
            }
            catch (Exception detachError)
            {
                (errors ??= []).Add(detachError);
            }
        }

        if (errors is not null)
        {
            throw new AggregateException("One or more imported trees could not be detached.", errors);
        }
    }

    /// <summary>Keeps one validated plan with the exact-tree insertion engine that owns it.</summary>
    private readonly record struct PreparedTree(
        NestedSetBulkInsert<TEntity, TKey, TTreeId, TScope> Insertion,
        NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope> Plan,
        NestedSetPlacementResolver<TEntity, TKey, TTreeId, TScope>.Destination Destination
    );
}

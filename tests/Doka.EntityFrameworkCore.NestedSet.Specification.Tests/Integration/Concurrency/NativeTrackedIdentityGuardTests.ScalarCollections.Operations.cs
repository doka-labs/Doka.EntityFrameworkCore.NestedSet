namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class NativeTrackedIdentityGuardTests
{
    public abstract partial class Scale
    {
        /// <summary>Changes persisted membership through another public context while retaining snapshots.</summary>
        /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
        private static Task MoveLastScalarCandidateAsync(
            DbContext reader,
            DbContext writer,
            bool convertedKeys,
            int parent,
            CancellationToken cancellationToken
        )
        {
            if (convertedKeys)
            {
                var last = reader
                    .ChangeTracker
                    .Entries<BroadTreeIdNode>()
                    .Last()
                    .Entity;

                return writer
                    .NestedSet<BroadTreeIdNode>()
                    .MoveToAsync(last.Id, parent, cancellationToken);
            }

            var leaf = reader
                .ChangeTracker
                .Entries<ScalarStringNode>()
                .Last()
                .Entity;

            return writer
                .NestedSet<ScalarStringNode>()
                .MoveToAsync(leaf.Id, ScalarKey(parent), cancellationToken);
        }

        /// <summary>Runs exact typed requests without changing payload, isolating guard query budgets.</summary>
        /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
        private static Task ExecuteScalarRequestsAsync(
            DbContext context,
            bool convertedKeys,
            Guid[] trees,
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken
        )
        {
            if (convertedKeys)
            {
                return ExecuteNativeRequestsAsync(context, false, trees, operation, cancellationToken);
            }

            var type = context.Model.FindEntityType(typeof(ScalarStringNode))!;
            var requests = trees
                .Select(tree => new NestedSetTreeLockRequest<Guid, NestedSetNoScope>(
                    type,
                    default,
                    tree,
                    NestedSetTreeLockMode.Existing))
                .ToArray();

            return new NestedSetMutationExecutor<ScalarStringNode, string, Guid, NestedSetNoScope>(context, type)
                .ExecuteAsync(operation, requests, cancellationToken);
        }
    }
}

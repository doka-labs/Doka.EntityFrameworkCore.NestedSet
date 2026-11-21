namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class NativeTrackedIdentityGuardTests
{
    public abstract partial class Scale
    {
        /// <summary>Runs an observed operation through the exact typed executor with 65 active tree locks.</summary>
        private static Task ExecuteNativeRequestsAsync(
            DbContext context,
            bool guidKeys,
            Guid[] treeIds,
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken
        )
        {
            if (guidKeys)
            {
                var entityType = context.Model.FindEntityType(typeof(NativeGuidGuardNode))!;
                var requests = treeIds
                    .Select(treeId => new NestedSetTreeLockRequest<Guid, NestedSetNoScope>(
                        entityType,
                        default,
                        treeId,
                        NestedSetTreeLockMode.Existing))
                    .ToArray();

                return new NestedSetMutationExecutor<NativeGuidGuardNode, Guid, Guid, NestedSetNoScope>(
                    context,
                    entityType).ExecuteAsync(operation, requests, cancellationToken);
            }

            var convertedEntity = context.Model.FindEntityType(typeof(BroadTreeIdNode))!;
            var convertedRequests = treeIds
                .Select(treeId => new NestedSetTreeLockRequest<BroadTreeId, NestedSetNoScope>(
                    convertedEntity,
                    default,
                    new BroadTreeId(treeId.ToString("N")),
                    NestedSetTreeLockMode.Existing))
                .ToArray();

            return new NestedSetMutationExecutor<BroadTreeIdNode, int, BroadTreeId, NestedSetNoScope>(
                context,
                convertedEntity).ExecuteAsync(operation, convertedRequests, cancellationToken);
        }

        /// <summary>Deletes through the public hierarchy API with the model's exact TreeId type.</summary>
        private static Task DeleteNativeScaleAsync(
            DbContext context,
            bool guidKeys,
            Guid treeId,
            CancellationToken cancellationToken
        ) => guidKeys
            ? context
                .NestedSet<NativeGuidGuardNode>()
                .DeleteTreeAsync(treeId, cancellationToken)
            : context
                .NestedSet<BroadTreeIdNode>()
                .DeleteTreeAsync(new BroadTreeId(treeId.ToString("N")), cancellationToken);
    }
}

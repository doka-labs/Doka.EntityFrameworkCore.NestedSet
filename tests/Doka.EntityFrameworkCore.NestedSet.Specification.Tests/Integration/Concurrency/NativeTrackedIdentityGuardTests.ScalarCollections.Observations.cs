namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class NativeTrackedIdentityGuardTests
{
    public abstract partial class Scale
    {
        /// <summary>Preserves every tracked identity, relationship, coordinate and ordinary payload value.</summary>
        private static ScalarState[] ScalarSnapshots(
            DbContext context,
            bool convertedKeys
        ) => (convertedKeys
                ? context
                    .ChangeTracker
                    .Entries<BroadTreeIdNode>()
                    .Select(entry => new ScalarState(
                        ScalarKey(entry.Entity.Id),
                        entry.Entity.TreeId.Value,
                        entry.Entity.ParentId is { } parent
                            ? ScalarKey(parent)
                            : null,
                        entry.Entity.Left,
                        entry.Entity.Right,
                        entry.Entity.Depth,
                        entry.Entity.Position,
                        entry.Entity.Name))
                : context
                    .ChangeTracker
                    .Entries<ScalarStringNode>()
                    .Select(entry => new ScalarState(
                        entry.Entity.Id,
                        entry.Entity.TreeId.ToString("N"),
                        entry.Entity.ParentId,
                        entry.Entity.Left,
                        entry.Entity.Right,
                        entry.Entity.Depth,
                        entry.Entity.Position,
                        entry.Entity.Name)))
            .OrderBy(state => state.Key, StringComparer.Ordinal)
            .ToArray();

        /// <summary>Reads persisted tree membership independently of the reader's stale tracked entities.</summary>
        /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
        private static Task<int> ScalarTreeCountAsync(
            DbContext context,
            bool convertedKeys,
            Guid treeId,
            CancellationToken cancellationToken
        ) => convertedKeys
            ? context
                .NestedSet<BroadTreeIdNode>()
                .InTree(new BroadTreeId(treeId.ToString("N")))
                .Nodes
                .CountAsync(cancellationToken)
            : context
                .NestedSet<ScalarStringNode>()
                .InTree(treeId)
                .Nodes
                .CountAsync(cancellationToken);

        /// <summary>Creates non-overlapping fixed-width string identities with identical provider ordering.</summary>
        private static string ScalarKey(
            int key
        ) => $"KEY-{key:D10}";
    }
}

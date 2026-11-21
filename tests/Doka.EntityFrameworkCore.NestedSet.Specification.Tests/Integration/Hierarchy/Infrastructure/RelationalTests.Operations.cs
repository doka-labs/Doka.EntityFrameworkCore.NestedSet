namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class RelationalTests
{
    /// <summary>Delays an independent tree creation until both contexts are ready.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task InsertAfterGateAsync(
        ScopedNestedSet<TreeNode, int> tree,
        int id,
        Task gate,
        CancellationToken cancellationToken
    )
    {
        await gate;
        await tree.InsertRootAsync(Node(id), Guid.NewGuid(), cancellationToken);
    }

    /// <summary>Dispatches exactly one mutation selected by a scope-isolation theory case.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static Task ApplyScopedMutationAsync(
        TreeContext context,
        int scope,
        string operation,
        CancellationToken cancellationToken
    )
    {
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(scope);

        return operation switch
        {
            "InsertRoot" => tree.InsertRootAsync(Node(7), Guid.NewGuid(), cancellationToken),
            "InsertFirstChild" => tree.InsertAsFirstChildAsync(Node(7), 1, cancellationToken),
            "InsertLastChild" => tree.InsertAsLastChildAsync(Node(7), 1, cancellationToken),
            "InsertBefore" => tree.InsertBeforeAsync(Node(7), 2, cancellationToken),
            "InsertAfter" => tree.InsertAfterAsync(Node(7), 2, cancellationToken),
            "MoveBefore" => tree.MoveBeforeAsync(2, 3, cancellationToken),
            "MoveAfter" => tree.MoveAfterAsync(2, 5, cancellationToken),
            "MoveFirstChild" => tree.MoveBeforeAsync(5, 6, cancellationToken),
            "MoveLastChild" => tree.MoveToAsync(3, 2, cancellationToken),
            "DetachAsTree" => tree.DetachAsTreeAsync(2, Guid.NewGuid(), cancellationToken),
            "Delete" => tree.DeleteAsync(2, cancellationToken),
            "DeleteSubtree" => tree.DeleteSubtreeAsync(2, cancellationToken),
            "Rebuild" => tree
                .InTree(Guid.Empty)
                .RebuildAsync(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
    }

    /// <summary>Corrupts derived values while retaining the ordered adjacency used by repair.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static Task<int> CorruptStructureAsync(
        TreeContext context,
        int scope,
        string corruption,
        CancellationToken cancellationToken
    )
    {
        var nodes = context
            .NestedSet<TreeNode>()
            .ForScope(scope)
            .InTree(Guid.Empty)
            .Nodes;

        return corruption switch
        {
            "Bounds" => nodes.ExecuteUpdateAsync(
                x => x
                    .SetProperty(n => n.Start, 1)
                    .SetProperty(n => n.End, 2),
                cancellationToken),
            "NegativeDepth" => nodes.ExecuteUpdateAsync(x => x.SetProperty(n => n.Depth, 0), cancellationToken),
            "ExcessiveDepth" => nodes.ExecuteUpdateAsync(x => x.SetProperty(n => n.Depth, 77), cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(corruption)),
        };
    }

    /// <summary>Creates one unrepairable adjacency defect without testing it during Arrange.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static Task<int> CorruptAdjacencyAsync(
        TreeContext context,
        int scope,
        string engine,
        string corruption,
        CancellationToken cancellationToken
    )
    {
        var nodes = context
            .Set<TreeNode>()
            .AsNoTracking()
            .Where(node => node.Tree == scope);

        return corruption switch
        {
            "Cycle" => nodes
                .Where(x => x.NodeId == 1)
                .ExecuteUpdateAsync(x => x.SetProperty(n => n.Parent, 2), cancellationToken),
            "Orphan" => CorruptOrphanAsync(context, scope, engine, cancellationToken: cancellationToken),
            "DuplicateChildPosition" => nodes
                .Where(x => x.NodeId == 3)
                .ExecuteUpdateAsync(x => x.SetProperty(n => n.Position, 0), cancellationToken),
            "MultipleRoots" => nodes
                .Where(x => x.NodeId == 6)
                .ExecuteUpdateAsync(
                    x => x
                        .SetProperty(n => n.Position, 0)
                        .SetProperty(n => n.Parent, (int?)null),
                    cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(corruption)),
        };
    }

    /// <summary>Creates external orphan corruption while restoring normal FK enforcement before Rebuild.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task<int> CorruptOrphanAsync(
        TreeContext context,
        int scope,
        string engine,
        CancellationToken cancellationToken
    )
    {
        var entity = context.Model.FindEntityType(typeof(TreeNode))!;
        var sql = context.GetService<ISqlGenerationHelper>();
        var table = sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
        var disable = engine switch
        {
            "Sqlite" => "PRAGMA foreign_keys = OFF",
            "MySql" or "MariaDb" => "SET FOREIGN_KEY_CHECKS = 0",
            "PostgreSql" => $"ALTER TABLE {table} DISABLE TRIGGER ALL",
            "SqlServer" => $"ALTER TABLE {table} NOCHECK CONSTRAINT ALL",
            _ => throw new ArgumentOutOfRangeException(nameof(engine)),
        };

        var enable = engine switch
        {
            "Sqlite" => "PRAGMA foreign_keys = ON",
            "MySql" or "MariaDb" => "SET FOREIGN_KEY_CHECKS = 1",
            "PostgreSql" => $"ALTER TABLE {table} ENABLE TRIGGER ALL",
            "SqlServer" => $"ALTER TABLE {table} CHECK CONSTRAINT ALL",
            _ => throw new ArgumentOutOfRangeException(nameof(engine)),
        };

        var wasOpen = context.Database.GetDbConnection()
                .State
            == ConnectionState.Open;

        if (!wasOpen)
        {
            await context.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            // WHY: The production model correctly prevents orphans. Rebuild must still diagnose externally
            // corrupted or adopted databases, so this test bypasses the FK only for the corrupting statement.
            await context.Database.ExecuteSqlRawAsync(disable, cancellationToken);

            return await context
                .Set<TreeNode>()
                .AsNoTracking()
                .Where(node => node.Tree == scope)
                .Where(node => node.NodeId == 2)
                .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.Parent, -1), cancellationToken);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync(enable, cancellationToken);

            if (!wasOpen)
            {
                await context.Database.CloseConnectionAsync();
            }
        }
    }
}
